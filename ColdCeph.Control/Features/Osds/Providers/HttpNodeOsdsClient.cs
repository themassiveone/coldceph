using System.Net.Http.Json;
using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Osds.Interfaces;
using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Control.Features.Osds.Providers;

/// <summary>
/// Issues OSD start/stop to a node over HTTP.
/// <para>
/// This fails closed. A non-2xx answer, an unparseable body or a timeout throws, so the caller
/// leaves inventory as it was and the next tick retries. It must never report back the state it
/// asked for: doing that let an unreachable node read as "every OSD is running", which takes the
/// plane to READY and admits S3 traffic against OSDs that never started.
/// </para>
/// </summary>
public sealed class HttpNodeOsdsClient : INodeOsdsClient
{
    private readonly IHttpClientFactory _factory;
    private readonly ControlConfig _config;

    public HttpNodeOsdsClient(IHttpClientFactory factory, ControlConfig config)
    {
        _factory = factory;
        _config = config;
    }

    public OsdMutationResult Start(Uri endpoint, OsdMutationRequest request)
        => Post(endpoint, $"/v1/osds/{request.OsdId}/start", request);

    public OsdMutationResult Stop(Uri endpoint, OsdMutationRequest request)
        => Post(endpoint, $"/v1/osds/{request.OsdId}/stop", request);

    private OsdMutationResult Post(Uri endpoint, string path, OsdMutationRequest request)
    {
        using var response = Client(endpoint).PostAsJsonAsync(path, request).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Node {endpoint} answered {(int)response.StatusCode} to {path}.");

        var result = response.Content.ReadFromJsonAsync<OsdMutationResult>().GetAwaiter().GetResult();
        return result ?? throw new InvalidOperationException(
            $"Node {endpoint} returned no OSD result for {path}; leaving the observation unchanged.");
    }

    private HttpClient Client(Uri endpoint)
    {
        var client = _factory.CreateClient("node");
        client.BaseAddress = endpoint;
        client.Timeout = _config.NodeCommandTimeout;
        client.DefaultRequestHeaders.Remove("X-ColdCeph-Token");
        client.DefaultRequestHeaders.Add("X-ColdCeph-Token", _config.NodeToken);
        return client;
    }
}

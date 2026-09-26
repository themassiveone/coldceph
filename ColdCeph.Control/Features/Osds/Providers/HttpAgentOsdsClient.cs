using System.Net.Http.Json;
using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Osds.Interfaces;
using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Control.Features.Osds.Providers;

public sealed class HttpAgentOsdsClient : IAgentOsdsClient
{
    private readonly IHttpClientFactory _factory;
    private readonly ControlConfig _config;

    public HttpAgentOsdsClient(IHttpClientFactory factory, ControlConfig config)
    {
        _factory = factory;
        _config = config;
    }

    public IReadOnlyList<OsdDto> List(Uri endpoint)
        => Client(endpoint).GetFromJsonAsync<IReadOnlyList<OsdDto>>("/v1/osds").GetAwaiter().GetResult() ?? [];

    public OsdMutationResult Start(Uri endpoint, OsdMutationRequest request)
        => Post(endpoint, $"/v1/osds/{request.OsdId}/start", request);

    public OsdMutationResult Stop(Uri endpoint, OsdMutationRequest request)
        => Post(endpoint, $"/v1/osds/{request.OsdId}/stop", request);

    private OsdMutationResult Post(Uri endpoint, string path, OsdMutationRequest request)
        => Client(endpoint).PostAsJsonAsync(path, request).GetAwaiter().GetResult()
            .Content.ReadFromJsonAsync<OsdMutationResult>().GetAwaiter().GetResult()
           ?? new OsdMutationResult(request.OsdId, request.DesiredRunning, false);

    private HttpClient Client(Uri endpoint)
    {
        var client = _factory.CreateClient("agent");
        client.BaseAddress = endpoint;
        client.DefaultRequestHeaders.Remove("X-ColdCeph-Token");
        client.DefaultRequestHeaders.Add("X-ColdCeph-Token", _config.AgentToken);
        return client;
    }
}

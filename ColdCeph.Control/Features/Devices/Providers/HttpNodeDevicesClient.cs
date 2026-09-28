using System.Net.Http.Json;
using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Devices.Interfaces;
using ColdCeph.Core.Features.Devices.DTOs;

namespace ColdCeph.Control.Features.Devices.Providers;

/// <summary>
/// Issues disk wake/standby to a node over HTTP.
/// <para>
/// Fails closed for the same reason as the OSD client: reporting back the requested power state
/// let an unreachable node read as "every disk is in standby", which takes the plane to COLD
/// while the disks are still spinning.
/// </para>
/// </summary>
public sealed class HttpNodeDevicesClient : INodeDevicesClient
{
    private readonly IHttpClientFactory _factory;
    private readonly ControlConfig _config;

    public HttpNodeDevicesClient(IHttpClientFactory factory, ControlConfig config)
    {
        _factory = factory;
        _config = config;
    }

    public DeviceMutationResult Wake(Uri endpoint, DeviceMutationRequest request)
        => Post(endpoint, $"/v1/devices/{request.DeviceId}/wake", request);

    public DeviceMutationResult Standby(Uri endpoint, DeviceMutationRequest request)
        => Post(endpoint, $"/v1/devices/{request.DeviceId}/standby", request);

    private DeviceMutationResult Post(Uri endpoint, string path, DeviceMutationRequest request)
    {
        using var response = Client(endpoint).PostAsJsonAsync(path, request).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Node {endpoint} answered {(int)response.StatusCode} to {path}.");

        var result = response.Content.ReadFromJsonAsync<DeviceMutationResult>().GetAwaiter().GetResult();
        return result ?? throw new InvalidOperationException(
            $"Node {endpoint} returned no device result for {path}; leaving the observation unchanged.");
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

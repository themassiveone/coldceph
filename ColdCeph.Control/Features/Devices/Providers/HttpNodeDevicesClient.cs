using System.Net.Http.Json;
using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Devices.Interfaces;
using ColdCeph.Core.Features.Devices.DTOs;

namespace ColdCeph.Control.Features.Devices.Providers;

public sealed class HttpNodeDevicesClient : INodeDevicesClient
{
    private readonly IHttpClientFactory _factory;
    private readonly ControlConfig _config;

    public HttpNodeDevicesClient(IHttpClientFactory factory, ControlConfig config)
    {
        _factory = factory;
        _config = config;
    }

    public IReadOnlyList<DeviceDto> List(Uri endpoint)
        => Client(endpoint).GetFromJsonAsync<IReadOnlyList<DeviceDto>>("/v1/devices").GetAwaiter().GetResult() ?? [];

    public DeviceMutationResult Wake(Uri endpoint, DeviceMutationRequest request)
        => Post(endpoint, $"/v1/devices/{request.DeviceId}/wake", request);

    public DeviceMutationResult Standby(Uri endpoint, DeviceMutationRequest request)
        => Post(endpoint, $"/v1/devices/{request.DeviceId}/standby", request);

    private DeviceMutationResult Post(Uri endpoint, string path, DeviceMutationRequest request)
        => Client(endpoint).PostAsJsonAsync(path, request).GetAwaiter().GetResult()
            .Content.ReadFromJsonAsync<DeviceMutationResult>().GetAwaiter().GetResult()
           ?? new DeviceMutationResult(request.DeviceId, request.DesiredPowerState, false);

    private HttpClient Client(Uri endpoint)
    {
        var client = _factory.CreateClient("node");
        client.BaseAddress = endpoint;
        client.DefaultRequestHeaders.Remove("X-ColdCeph-Token");
        client.DefaultRequestHeaders.Add("X-ColdCeph-Token", _config.NodeToken);
        return client;
    }
}

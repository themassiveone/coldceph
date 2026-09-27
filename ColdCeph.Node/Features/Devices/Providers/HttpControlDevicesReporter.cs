using System.Net.Http.Json;
using ColdCeph.Node.Composition;
using ColdCeph.Node.Features.Devices.Interfaces;
using ColdCeph.Core.Features.Devices.DTOs;

namespace ColdCeph.Node.Features.Devices.Providers;

public sealed class HttpControlDevicesReporter : IControlDevicesReporter
{
    private readonly IHttpClientFactory _factory;
    private readonly NodeConfig _config;

    public HttpControlDevicesReporter(IHttpClientFactory factory, NodeConfig config)
    {
        _factory = factory;
        _config = config;
    }

    public void Send(HostDevicesObservationDto observation)
    {
        if (_config.ControlEndpoint is null)
            return;

        var client = _factory.CreateClient("control");
        client.BaseAddress = _config.ControlEndpoint;
        client.DefaultRequestHeaders.Remove("X-ColdCeph-Token");
        client.DefaultRequestHeaders.Add("X-ColdCeph-Token", _config.NodeToken);
        var response = client.PostAsJsonAsync("/v1/devices/observed", observation).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
    }
}

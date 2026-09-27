using System.Net.Http.Json;
using ColdCeph.Node.Composition;
using ColdCeph.Node.Features.Osds.Interfaces;
using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Node.Features.Osds.Providers;

public sealed class HttpControlOsdsReporter : IControlOsdsReporter
{
    private readonly IHttpClientFactory _factory;
    private readonly NodeConfig _config;

    public HttpControlOsdsReporter(IHttpClientFactory factory, NodeConfig config)
    {
        _factory = factory;
        _config = config;
    }

    public void Send(HostOsdsObservationDto observation)
    {
        if (_config.ControlEndpoint is null)
            return;

        var client = _factory.CreateClient("control");
        client.BaseAddress = _config.ControlEndpoint;
        client.DefaultRequestHeaders.Remove("X-ColdCeph-Token");
        client.DefaultRequestHeaders.Add("X-ColdCeph-Token", _config.NodeToken);
        var response = client.PostAsJsonAsync("/v1/osds/observed", observation).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
    }
}

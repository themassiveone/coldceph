using System.Net.Http.Json;
using ColdCeph.Node.Composition;
using ColdCeph.Node.Features.Hosts.Interfaces;
using ColdCeph.Core.Features.Hosts.DTOs;

namespace ColdCeph.Node.Features.Hosts.Providers;

public sealed class HttpControlHeartbeatClient : IControlHeartbeatClient
{
    private readonly IHttpClientFactory _factory;
    private readonly NodeConfig _config;

    public HttpControlHeartbeatClient(IHttpClientFactory factory, NodeConfig config)
    {
        _factory = factory;
        _config = config;
    }

    public void Send(NodeStatusDto status, Uri advertiseEndpoint)
    {
        if (_config.ControlEndpoint is null)
            return;

        var client = _factory.CreateClient("control");
        client.BaseAddress = _config.ControlEndpoint;
        client.DefaultRequestHeaders.Remove("X-ColdCeph-Node-Endpoint");
        client.DefaultRequestHeaders.Add("X-ColdCeph-Node-Endpoint", advertiseEndpoint.ToString());
        var response = client.PostAsJsonAsync("/v1/hosts/join", status).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
    }
}

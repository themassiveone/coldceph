using System.Net.Http.Json;
using ColdCeph.Agent.Composition;
using ColdCeph.Agent.Features.Hosts.Interfaces;
using ColdCeph.Core.Features.Hosts.DTOs;

namespace ColdCeph.Agent.Features.Hosts.Providers;

public sealed class HttpControlHeartbeatClient : IControlHeartbeatClient
{
    private readonly IHttpClientFactory _factory;
    private readonly AgentConfig _config;

    public HttpControlHeartbeatClient(IHttpClientFactory factory, AgentConfig config)
    {
        _factory = factory;
        _config = config;
    }

    public void Send(AgentStatusDto status, Uri advertiseEndpoint)
    {
        if (_config.ControlEndpoint is null)
            return;

        var client = _factory.CreateClient("control");
        client.BaseAddress = _config.ControlEndpoint;
        client.DefaultRequestHeaders.Remove("X-ColdCeph-Agent-Endpoint");
        client.DefaultRequestHeaders.Add("X-ColdCeph-Agent-Endpoint", advertiseEndpoint.ToString());
        var response = client.PostAsJsonAsync("/v1/hosts/join", status).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
    }
}

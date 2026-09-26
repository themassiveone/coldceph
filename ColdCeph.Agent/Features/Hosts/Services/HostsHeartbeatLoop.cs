using ColdCeph.Agent.Composition;
using ColdCeph.Agent.Features.Hosts.Controllers;
using ColdCeph.Agent.Features.Hosts.Interfaces;
using Microsoft.Extensions.Hosting;

namespace ColdCeph.Agent.Features.Hosts.Services;

public sealed class HostsHeartbeatLoop : BackgroundService
{
    private readonly HostsController _hosts;
    private readonly IControlHeartbeatClient _client;
    private readonly AgentConfig _config;

    public HostsHeartbeatLoop(HostsController hosts, IControlHeartbeatClient client, AgentConfig config)
    {
        _hosts = hosts;
        _client = client;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            BeatOnce();
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    public void BeatOnce()
    {
        if (_config.ControlEndpoint is null)
            return;

        try
        {
            _client.Send(_hosts.GetStatus(), _config.AdvertiseEndpoint);
        }
        catch (Exception)
        {
            // Control being down must not stop the agent.
        }
    }
}

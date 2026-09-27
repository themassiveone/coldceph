using ColdCeph.Node.Composition;
using ColdCeph.Node.Features.Hosts.Controllers;
using ColdCeph.Node.Features.Hosts.Interfaces;
using Microsoft.Extensions.Hosting;

namespace ColdCeph.Node.Features.Hosts.Services;

public sealed class HostsHeartbeatLoop : BackgroundService
{
    private readonly HostsController _hosts;
    private readonly IControlHeartbeatClient _client;
    private readonly NodeConfig _config;

    public HostsHeartbeatLoop(HostsController hosts, IControlHeartbeatClient client, NodeConfig config)
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
            var delay = _hosts.IsControlEnrolled() ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(1);
            await Task.Delay(delay, stoppingToken);
        }
    }

    public void BeatOnce()
    {
        if (_config.ControlEndpoint is null)
            return;

        try
        {
            _hosts.NoteJoinStatus(_client.Send(_hosts.GetStatus(), _config.AdvertiseEndpoint));
        }
        catch (Exception)
        {
            // Control being down must not stop the node.
        }
    }
}

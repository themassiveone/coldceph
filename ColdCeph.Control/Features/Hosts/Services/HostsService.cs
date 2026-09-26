using ColdCeph.Control.Composition;
using ColdCeph.Control.Shared;
using ColdCeph.Core.Features.Hosts.DTOs;

namespace ColdCeph.Control.Features.Hosts.Services;

public sealed class HostsService
{
    private readonly ControlConfig _config;
    private readonly IClock _clock;
    private readonly Dictionary<string, HostDto> _hosts = new(StringComparer.Ordinal);

    public HostsService(ControlConfig config, IClock clock)
    {
        _config = config;
        _clock = clock;
    }

    public IReadOnlyList<HostDto> ListHosts()
        => _hosts.Values.Select(Refresh).ToArray();

    public HostDto? GetHost(string hostId)
        => _hosts.TryGetValue(hostId, out var host) ? Refresh(host) : null;

    public HostDto RegisterHeartbeat(AgentStatusDto status, Uri endpoint)
    {
        var host = new HostDto
        {
            HostId = status.HostId,
            Hostname = status.Hostname,
            Endpoint = endpoint,
            LastHeartbeat = _clock.UtcNow,
            Alive = true
        };
        _hosts[status.HostId] = host;
        return host;
    }

    private HostDto Refresh(HostDto host)
        => host with { Alive = _clock.UtcNow - host.LastHeartbeat <= _config.HeartbeatStaleAfter };
}

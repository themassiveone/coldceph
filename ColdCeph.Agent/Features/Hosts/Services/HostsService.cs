using ColdCeph.Agent.Composition;
using ColdCeph.Core.Features.Hosts.DTOs;

namespace ColdCeph.Agent.Features.Hosts.Services;

public sealed class HostsService
{
    private readonly AgentConfig _config;

    public HostsService(AgentConfig config)
    {
        _config = config;
    }

    public AgentStatusDto GetStatus()
        => new()
        {
            HostId = _config.HostId,
            Hostname = _config.Hostname,
            ObservedAt = DateTimeOffset.UtcNow
        };
}

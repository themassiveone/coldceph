using ColdCeph.Node.Composition;
using ColdCeph.Core.Features.Hosts.DTOs;

namespace ColdCeph.Node.Features.Hosts.Services;

public sealed class HostsService
{
    private readonly NodeConfig _config;
    private bool _enrolled;

    public HostsService(NodeConfig config)
    {
        _config = config;
    }

    public NodeStatusDto GetStatus()
        => new()
        {
            HostId = _config.HostId,
            Hostname = _config.Hostname,
            ObservedAt = DateTimeOffset.UtcNow
        };

    public bool IsControlEnrolled() => _enrolled;

    public void NoteJoinStatus(int statusCode) => _enrolled = statusCode == 200;
}

using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Hosts.Interfaces;
using ColdCeph.Control.Features.Hosts.Models;
using ColdCeph.Control.Shared;
using ColdCeph.Core.Features.Hosts.DTOs;

namespace ColdCeph.Control.Features.Hosts.Services;

public sealed class HostsService
{
    private readonly ControlConfig _config;
    private readonly IClock _clock;
    private readonly IHostsRepository? _repository;
    private readonly Dictionary<string, HostDto> _hosts;
    private readonly Dictionary<string, HostJoinRequestDto> _pending;
    private readonly Dictionary<string, HostJoinRequestDto> _blocked;

    public HostsService(ControlConfig config, IClock clock, IHostsRepository? repository = null)
    {
        _config = config;
        _clock = clock;
        _repository = repository;
        var loaded = repository?.Load() ?? new HostsRecord();
        _hosts = loaded.Hosts;
        _pending = loaded.Pending;
        _blocked = loaded.Blocked;
        for (var index = 0; index < config.ConfiguredNodeEndpoints.Count; index++)
        {
            var hostId = config.ConfiguredNodeEndpoints.Count == 1
                ? config.ConfiguredNodeHostId
                : $"{config.ConfiguredNodeHostId}-{index + 1}";
            if (_hosts.ContainsKey(hostId))
                continue;
            RegisterHeartbeat(
                new NodeStatusDto { HostId = hostId, Hostname = hostId, ObservedAt = clock.UtcNow },
                config.ConfiguredNodeEndpoints[index]);
        }
    }

    public IReadOnlyList<HostDto> ListHosts()
        => _hosts.Values.Select(Refresh).ToArray();

    public IReadOnlyList<HostJoinRequestDto> ListPendingJoins()
        => _pending.Values.ToArray();

    public IReadOnlyList<HostJoinRequestDto> ListBlockedJoins()
        => _blocked.Values.ToArray();

    public HostDto? GetHost(string hostId)
        => _hosts.TryGetValue(hostId, out var host) ? Refresh(host) : null;

    public HostDto RegisterHeartbeat(NodeStatusDto status, Uri endpoint)
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
        _pending.Remove(status.HostId);
        _blocked.Remove(status.HostId);
        Persist();
        return host;
    }

    public NodeJoinResult RequestJoin(NodeStatusDto status, string? advertisedEndpoint)
    {
        if (string.IsNullOrWhiteSpace(status.HostId)
            || !Uri.TryCreate(advertisedEndpoint, UriKind.Absolute, out var endpoint))
            return new NodeJoinResult(400, null);

        var request = new HostJoinRequestDto
        {
            HostId = status.HostId,
            Hostname = status.Hostname,
            Endpoint = endpoint,
            RequestedAt = _clock.UtcNow
        };

        if (_hosts.ContainsKey(status.HostId))
            return new NodeJoinResult(200, RegisterHeartbeat(status, endpoint));

        if (_blocked.ContainsKey(status.HostId))
        {
            _blocked[status.HostId] = request;
            Persist();
            return new NodeJoinResult(403, null);
        }

        _pending[status.HostId] = request;
        Persist();
        return new NodeJoinResult(202, null);
    }

    public HostDto? Approve(string hostId)
    {
        if (_pending.Remove(hostId, out var pending))
            return RegisterHeartbeat(ToStatus(pending), pending.Endpoint);
        if (_blocked.Remove(hostId, out var blocked))
            return RegisterHeartbeat(ToStatus(blocked), blocked.Endpoint);
        return null;
    }

    public bool Deny(string hostId)
    {
        if (!_pending.Remove(hostId, out var pending))
            return false;
        _blocked[hostId] = pending;
        Persist();
        return true;
    }

    private void Persist()
        => _repository?.Save(new HostsRecord
        {
            Hosts = new Dictionary<string, HostDto>(_hosts, StringComparer.Ordinal),
            Pending = new Dictionary<string, HostJoinRequestDto>(_pending, StringComparer.Ordinal),
            Blocked = new Dictionary<string, HostJoinRequestDto>(_blocked, StringComparer.Ordinal)
        });

    private static NodeStatusDto ToStatus(HostJoinRequestDto request)
        => new()
        {
            HostId = request.HostId,
            Hostname = request.Hostname,
            ObservedAt = request.RequestedAt
        };

    private HostDto Refresh(HostDto host)
        => host with { Alive = _clock.UtcNow - host.LastHeartbeat <= _config.HeartbeatStaleAfter };
}

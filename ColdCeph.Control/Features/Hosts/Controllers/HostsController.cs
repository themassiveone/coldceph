using ColdCeph.Control.Features.Hosts.Models;
using ColdCeph.Control.Features.Hosts.Services;
using ColdCeph.Core.Features.Hosts.DTOs;

namespace ColdCeph.Control.Features.Hosts.Controllers;

public sealed class HostsController
{
    private readonly HostsService _service;

    public HostsController(HostsService service)
    {
        _service = service;
    }

    public IReadOnlyList<HostDto> ListHosts() => _service.ListHosts();

    public IReadOnlyList<HostJoinRequestDto> ListPendingJoins() => _service.ListPendingJoins();

    public IReadOnlyList<HostJoinRequestDto> ListBlockedJoins() => _service.ListBlockedJoins();

    public HostDto? GetHost(string hostId) => _service.GetHost(hostId);

    public HostDto RegisterHeartbeat(NodeStatusDto status, Uri endpoint)
        => _service.RegisterHeartbeat(status, endpoint);

    public NodeJoinResult RequestJoin(NodeStatusDto status, string? advertisedEndpoint)
        => _service.RequestJoin(status, advertisedEndpoint);

    public HostDto? Approve(string hostId) => _service.Approve(hostId);

    public bool Deny(string hostId) => _service.Deny(hostId);
}

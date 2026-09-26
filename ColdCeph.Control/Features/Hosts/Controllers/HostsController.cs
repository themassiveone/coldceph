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

    public HostDto? GetHost(string hostId) => _service.GetHost(hostId);

    public HostDto RegisterHeartbeat(AgentStatusDto status, Uri endpoint)
        => _service.RegisterHeartbeat(status, endpoint);
}

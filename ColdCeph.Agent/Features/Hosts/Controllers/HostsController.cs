using ColdCeph.Agent.Features.Hosts.Services;
using ColdCeph.Core.Features.Hosts.DTOs;

namespace ColdCeph.Agent.Features.Hosts.Controllers;

public sealed class HostsController
{
    private readonly HostsService _service;

    public HostsController(HostsService service)
    {
        _service = service;
    }

    public AgentStatusDto GetStatus() => _service.GetStatus();
}

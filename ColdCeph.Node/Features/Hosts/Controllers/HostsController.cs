using ColdCeph.Node.Features.Hosts.Services;
using ColdCeph.Core.Features.Hosts.DTOs;

namespace ColdCeph.Node.Features.Hosts.Controllers;

public sealed class HostsController
{
    private readonly HostsService _service;

    public HostsController(HostsService service)
    {
        _service = service;
    }

    public NodeStatusDto GetStatus() => _service.GetStatus();

    public bool IsControlEnrolled() => _service.IsControlEnrolled();

    public void NoteJoinStatus(int statusCode) => _service.NoteJoinStatus(statusCode);
}

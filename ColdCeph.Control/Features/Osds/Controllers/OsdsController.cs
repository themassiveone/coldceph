using ColdCeph.Control.Features.Osds.Services;
using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Control.Features.Osds.Controllers;

public sealed class OsdsController
{
    private readonly OsdsService _service;

    public OsdsController(OsdsService service)
    {
        _service = service;
    }

    public IReadOnlyList<OsdDto> ListOsds() => _service.ListOsds();

    public OsdDto? GetOsd(int osdId) => _service.GetOsd(osdId);

    public bool IsEveryProcessRunning() => _service.IsEveryProcessRunning();

    public bool IsEveryProcessStopped() => _service.IsEveryProcessStopped();
}

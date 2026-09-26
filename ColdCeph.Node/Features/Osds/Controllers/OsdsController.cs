using ColdCeph.Node.Features.Osds.Services;
using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Node.Features.Osds.Controllers;

public sealed class OsdsController
{
    private readonly OsdsService _service;

    public OsdsController(OsdsService service)
    {
        _service = service;
    }

    public IReadOnlyList<OsdDto> ListOsds() => _service.ListOsds();

    public OsdDto? GetOsd(int osdId) => _service.GetOsd(osdId);

    public OsdMutationResult Start(OsdMutationRequest request) => _service.Start(request);

    public OsdMutationResult Stop(OsdMutationRequest request) => _service.Stop(request);
}

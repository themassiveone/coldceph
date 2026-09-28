using ColdCeph.Control.Features.Integrity.Controllers;
using ColdCeph.Control.Features.Osds.Services;
using ColdCeph.Core.Features.Integrity.DTOs;
using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Control.Features.Osds.Controllers;

public sealed class OsdsController
{
    private readonly OsdsService _service;
    private readonly IntegrityController _integrity;

    public OsdsController(OsdsService service, IntegrityController integrity)
    {
        _service = service;
        _integrity = integrity;
    }

    public IReadOnlyList<OsdDto> ListOsds()
    {
        var membership = _integrity.ListOsdMembership();
        return _service.ListOsds().Select(osd => Overlay(osd, membership)).ToArray();
    }

    public IReadOnlyList<OsdDto> ListObservedOsds() => _service.ListOsds();

    public OsdDto? GetOsd(int osdId)
    {
        var osd = _service.GetOsd(osdId);
        if (osd is null)
            return null;
        return Overlay(osd, _integrity.ListOsdMembership());
    }

    public IReadOnlyList<string> ListObservationErrors() => _service.ListObservationErrors();

    public bool IsEveryProcessRunning() => _service.IsEveryProcessRunning();

    public bool IsEveryProcessStopped() => _service.IsEveryProcessStopped();

    public void ApplyObserved(HostOsdsObservationDto observation) => _service.ApplyObserved(observation);

    private static OsdDto Overlay(OsdDto osd, IReadOnlyDictionary<int, OsdMembershipDto> membership)
    {
        if (!membership.TryGetValue(osd.OsdId, out var ceph))
            return osd;
        return osd with { Up = ceph.Up, In = ceph.In };
    }
}

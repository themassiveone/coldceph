using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Integrity.Controllers;
using ColdCeph.Control.Features.Integrity.Services;
using ColdCeph.Control.Features.Osds.Controllers;
using ColdCeph.Control.Features.Osds.Services;
using ColdCeph.Control.Features.StoragePlane.Controllers;
using ColdCeph.Control.Features.StoragePlane.Services;
using ColdCeph.Control.Tests.Fake;
using ColdCeph.Core.Features.Integrity.DTOs;
using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Control.Tests.Features.Osds.Controller;

[TestFixture]
public sealed class OsdsControllerTests
{
    [Test]
    public void ListOsds_overlays_ceph_up_and_in()
    {
        var (osds, ceph) = Create();
        osds.ApplyObserved(new HostOsdsObservationDto
        {
            HostId = "node-a",
            Osds = [new OsdDto { OsdId = 0, HostId = "node-a", DeviceId = "d0", Up = false, In = true, ProcessRunning = false }]
        });
        ceph.OsdMembership = new Dictionary<int, OsdMembershipDto>
        {
            [0] = new() { OsdId = 0, Up = true, In = true }
        };

        var listed = osds.ListOsds().Single();

        Assert.That(listed.Up, Is.True);
        Assert.That(listed.In, Is.True);
        Assert.That(listed.ProcessRunning, Is.False);
        Assert.That(ceph.MembershipCalls, Is.EqualTo(1));
    }

    [Test]
    public void IsEveryProcessRunning_does_not_query_ceph()
    {
        var (osds, ceph) = Create();
        osds.ApplyObserved(new HostOsdsObservationDto
        {
            HostId = "node-a",
            Osds = [new OsdDto { OsdId = 0, HostId = "node-a", DeviceId = "d0", Up = false, In = true, ProcessRunning = true }]
        });

        Assert.That(osds.IsEveryProcessRunning(), Is.True);
        Assert.That(ceph.MembershipCalls, Is.EqualTo(0));
        Assert.That(ceph.ObservationCalls, Is.EqualTo(0));
    }

    [Test]
    public void ListOsds_keeps_node_process_state_when_ceph_has_no_membership()
    {
        var (osds, _) = Create();
        osds.ApplyObserved(new HostOsdsObservationDto
        {
            HostId = "node-a",
            Osds = [new OsdDto { OsdId = 0, HostId = "node-a", DeviceId = "d0", Up = false, In = true, ProcessRunning = true }]
        });

        var listed = osds.ListOsds().Single();

        Assert.That(listed.Up, Is.False);
        Assert.That(listed.ProcessRunning, Is.True);
    }

    private static (OsdsController Osds, FakeCephQueryProvider Ceph) Create()
    {
        var clock = new FakeClock();
        var ceph = new FakeCephQueryProvider();
        var plane = new StoragePlaneService(new MemoryStoragePlaneRepository(), new RecordingNooutProvider(), clock, new ControlConfig());
        var integrity = new IntegrityController(new IntegrityService(ceph, new MemoryIntegrityRepository(), new StoragePlaneController(plane), clock));
        var osds = new OsdsController(new OsdsService(new FakeNodeOsdsClient(), new ControlConfig()), integrity);
        return (osds, ceph);
    }
}

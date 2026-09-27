using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Osds.Services;
using ColdCeph.Control.Tests.Fake;
using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Control.Tests.Features.Osds.Unit;

[TestFixture]
public sealed class OsdsServiceTests
{
    [Test]
    public void ApplyObserved_replaces_one_host_without_dropping_another()
    {
        var osds = new OsdsService(new FakeNodeOsdsClient(), new ControlConfig());
        osds.ApplyObserved(Observation("node-a", 0));
        osds.ApplyObserved(Observation("node-b", 0));

        osds.ApplyObserved(new HostOsdsObservationDto
        {
            HostId = "node-a",
            Osds = [new OsdDto { OsdId = 1, HostId = "node-a", DeviceId = "d1", Up = false, In = true, ProcessRunning = false }]
        });

        Assert.That(osds.ListOsds().Count(osd => osd.HostId == "node-a"), Is.EqualTo(1));
        Assert.That(osds.ListOsds().Single(osd => osd.HostId == "node-a").OsdId, Is.EqualTo(1));
        Assert.That(osds.ListOsds().Single(osd => osd.HostId == "node-b").OsdId, Is.EqualTo(0));
    }

    [Test]
    public void ApplyObserved_does_not_keep_another_host_osd_under_the_same_id()
    {
        var osds = new OsdsService(new FakeNodeOsdsClient(), new ControlConfig());
        osds.ApplyObserved(Observation("node-a", 0));

        Assert.That(osds.ListOsds().All(osd => osd.HostId == "node-a"), Is.True);
        Assert.That(osds.ListOsds().Any(osd => osd.HostId == "node-b"), Is.False);
    }

    [Test]
    public void ApplyObserved_records_a_discovery_error()
    {
        var osds = new OsdsService(new FakeNodeOsdsClient(), new ControlConfig());
        osds.ApplyObserved(new HostOsdsObservationDto
        {
            HostId = "node-a",
            Osds = [],
            Error = "docker exec failed"
        });

        Assert.That(osds.ListObservationErrors(), Does.Contain("node-a: docker exec failed"));
    }

    [Test]
    public void ApplyObserved_clears_a_discovery_error_when_the_next_report_is_clean()
    {
        var osds = new OsdsService(new FakeNodeOsdsClient(), new ControlConfig());
        osds.ApplyObserved(new HostOsdsObservationDto { HostId = "node-a", Osds = [], Error = "docker exec failed" });
        osds.ApplyObserved(Observation("node-a", 0));

        Assert.That(osds.ListObservationErrors(), Is.Empty);
    }

    private static HostOsdsObservationDto Observation(string hostId, int osdId)
        => new()
        {
            HostId = hostId,
            Osds =
            [
                new OsdDto
                {
                    OsdId = osdId,
                    HostId = hostId,
                    DeviceId = $"{hostId}-d",
                    Up = false,
                    In = true,
                    ProcessRunning = false
                }
            ]
        };
}

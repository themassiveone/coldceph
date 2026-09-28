using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Osds.Repositories;
using ColdCeph.Control.Features.Osds.Services;
using ColdCeph.Control.Tests.Fake;
using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Control.Tests.Features.Osds.Unit;

[TestFixture]
public sealed class OsdsPersistenceTests
{
    [Test]
    public void Observed_osds_are_present_after_reload()
    {
        var dir = Path.Join(Path.GetTempPath(), "coldceph-tests", Guid.NewGuid().ToString("N"));
        var config = new ControlConfig { DataDirectory = dir };
        var first = new OsdsService(new FakeNodeOsdsClient(), config, new SqliteOsdsObservationRepository(config));
        first.ApplyObserved(new HostOsdsObservationDto
        {
            HostId = "node-a",
            Osds =
            [
                new OsdDto
                {
                    OsdId = 1,
                    HostId = "node-a",
                    DeviceId = "disk-a",
                    Up = true,
                    In = true,
                    ProcessRunning = true
                }
            ]
        });

        var second = new OsdsService(new FakeNodeOsdsClient(), config, new SqliteOsdsObservationRepository(config));

        Assert.That(second.ListOsds(), Has.Count.EqualTo(1));
        Assert.That(second.ListOsds()[0].OsdId, Is.EqualTo(1));
        Assert.That(second.ListOsds()[0].ProcessRunning, Is.True);
    }

    [Test]
    public void Discovery_error_is_present_after_reload()
    {
        var dir = Path.Join(Path.GetTempPath(), "coldceph-tests", Guid.NewGuid().ToString("N"));
        var config = new ControlConfig { DataDirectory = dir };
        var first = new OsdsService(new FakeNodeOsdsClient(), config, new SqliteOsdsObservationRepository(config));
        first.ApplyObserved(new HostOsdsObservationDto
        {
            HostId = "node-a",
            Osds = [],
            Error = "docker exec failed"
        });

        var second = new OsdsService(new FakeNodeOsdsClient(), config, new SqliteOsdsObservationRepository(config));

        Assert.That(second.ListObservationErrors(), Does.Contain("node-a: docker exec failed"));
    }
}

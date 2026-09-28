using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Devices.Repositories;
using ColdCeph.Control.Features.Devices.Services;
using ColdCeph.Control.Tests.Fake;
using ColdCeph.Core.Features.Devices.DTOs;

namespace ColdCeph.Control.Tests.Features.Devices.Unit;

[TestFixture]
public sealed class DevicesPersistenceTests
{
    [Test]
    public void Observed_devices_are_present_after_reload()
    {
        var dir = Path.Join(Path.GetTempPath(), "coldceph-tests", Guid.NewGuid().ToString("N"));
        var config = new ControlConfig { DataDirectory = dir };
        var first = new DevicesService(new FakeNodeDevicesClient(), config, new SqliteDevicesObservationRepository(config));
        first.ApplyObserved(new HostDevicesObservationDto
        {
            HostId = "node-a",
            Devices =
            [
                new DeviceDto
                {
                    DeviceId = "disk-a",
                    HostId = "node-a",
                    MappedOsdId = 1,
                    Wwn = "wwn",
                    Serial = "serial",
                    Path = "/dev/sda",
                    PowerState = DevicePowerState.Standby
                }
            ]
        });

        var second = new DevicesService(new FakeNodeDevicesClient(), config, new SqliteDevicesObservationRepository(config));

        Assert.That(second.ListDevices(), Has.Count.EqualTo(1));
        Assert.That(second.ListDevices()[0].DeviceId, Is.EqualTo("disk-a"));
        Assert.That(second.ListDevices()[0].PowerState, Is.EqualTo(DevicePowerState.Standby));
    }

    [Test]
    public void Empty_reload_does_not_invent_devices()
    {
        var dir = Path.Join(Path.GetTempPath(), "coldceph-tests", Guid.NewGuid().ToString("N"));
        var config = new ControlConfig { DataDirectory = dir };
        _ = new DevicesService(new FakeNodeDevicesClient(), config, new SqliteDevicesObservationRepository(config));

        var second = new DevicesService(new FakeNodeDevicesClient(), config, new SqliteDevicesObservationRepository(config));

        Assert.That(second.ListDevices(), Is.Empty);
    }
}

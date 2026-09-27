using ColdCeph.Core.Features.Devices.DTOs;
using ColdCeph.Node.Features.Devices.Providers;

namespace ColdCeph.Node.Tests.Features.Devices.Provider;

[TestFixture]
public sealed class MemoryDiskPowerTests
{
    [Test]
    public void Wake_then_read_is_active()
    {
        var power = new MemoryDiskPower();
        power.Standby("/mnt/ramdisk/osd.img");
        power.Wake("/mnt/ramdisk/osd.img");

        Assert.That(power.GetPowerState("d0", "/mnt/ramdisk/osd.img"), Is.EqualTo(DevicePowerState.Active));
    }

    [Test]
    public void Standby_does_not_change_a_different_path()
    {
        var power = new MemoryDiskPower();
        power.Standby("/dev/sda");

        Assert.That(power.GetPowerState("d1", "/mnt/ramdisk/osd.img"), Is.EqualTo(DevicePowerState.Active));
    }
}

using ColdCeph.Agent.Composition;
using ColdCeph.Agent.Features.Devices.Services;
using ColdCeph.Agent.Features.Osds.Controllers;
using ColdCeph.Agent.Features.Osds.Services;
using ColdCeph.Agent.Tests.Fake;
using ColdCeph.Core.Features.Devices.DTOs;
using ColdCeph.Core.Features.Operations.DTOs;
using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Agent.Tests.Features.Devices.Unit;

[TestFixture]
public sealed class DevicesServiceTests
{
    [Test]
    public void Standby_succeeds_when_mapped_osd_is_stopped()
    {
        var (devices, osds, power, runtime) = Create();
        runtime.Running.Clear();
        osds.Seed(Osd(running: false));
        devices.Seed(Device());

        var result = devices.Standby(Mutation(DevicePowerState.Standby));

        Assert.That(result.PowerState, Is.EqualTo(DevicePowerState.Standby));
        Assert.That(power.Commands, Does.Contain("standby"));
    }

    [Test]
    public void Standby_is_refused_while_mapped_osd_is_running()
    {
        var (devices, osds, power, runtime) = Create();
        runtime.Running.Add(1);
        osds.Seed(Osd(running: true));
        devices.Seed(Device());

        Assert.That(() => devices.Standby(Mutation(DevicePowerState.Standby)), Throws.InvalidOperationException);
        Assert.That(power.Commands, Does.Not.Contain("standby"));
    }

    private static (DevicesService Devices, OsdsService Osds, FakeDiskPower Power, FakeOsdRuntime Runtime) Create()
    {
        var config = new AgentConfig { HostId = "h1", Hostname = "h1" };
        var runtime = new FakeOsdRuntime();
        var osds = new OsdsService(runtime, config);
        var power = new FakeDiskPower();
        var devices = new DevicesService(power, new OsdsController(osds), config);
        return (devices, osds, power, runtime);
    }

    private static OsdDto Osd(bool running)
        => new()
        {
            OsdId = 1,
            HostId = "h1",
            DeviceId = "d1",
            Up = running,
            In = true,
            ProcessRunning = running
        };

    private static DeviceDto Device()
        => new()
        {
            DeviceId = "d1",
            HostId = "h1",
            MappedOsdId = 1,
            Wwn = "wwn",
            Serial = "serial",
            Path = "/dev/sda",
            PowerState = DevicePowerState.Active
        };

    private static DeviceMutationRequest Mutation(DevicePowerState desired)
        => new()
        {
            DeviceId = "d1",
            OperationId = OperationIdRules.Create().Value,
            ControllerIdentity = "control",
            Deadline = DateTimeOffset.UtcNow.AddMinutes(1),
            DesiredPowerState = desired
        };
}

using ColdCeph.Node.Composition;
using ColdCeph.Node.Features.Devices.Controllers;
using ColdCeph.Node.Features.Devices.Interfaces;
using ColdCeph.Node.Features.Devices.Services;
using ColdCeph.Node.Features.Hosts.Controllers;
using ColdCeph.Node.Features.Hosts.Services;
using ColdCeph.Node.Features.Osds.Controllers;
using ColdCeph.Node.Features.Osds.Services;
using ColdCeph.Node.Tests.Fake;
using ColdCeph.Core.Features.Devices.DTOs;

namespace ColdCeph.Node.Tests.Features.Devices.Unit;

[TestFixture]
public sealed class DevicesReportLoopTests
{
    [Test]
    public void ReportOnce_sends_inventory_after_enroll()
    {
        var (loop, reporter, _, hosts) = Create();
        hosts.NoteJoinStatus(200);

        loop.ReportOnce();

        Assert.That(reporter.Sent, Has.Count.EqualTo(1));
        Assert.That(reporter.Sent[0].Devices.Select(device => device.DeviceId), Does.Contain("d0"));
    }

    [Test]
    public void ReportOnce_does_not_send_when_not_enrolled()
    {
        var (loop, reporter, _, _) = Create();

        loop.ReportOnce();

        Assert.That(reporter.Sent, Is.Empty);
    }

    [Test]
    public void ReportOnce_does_not_resend_an_unchanged_snapshot()
    {
        var (loop, reporter, _, hosts) = Create();
        hosts.NoteJoinStatus(200);

        loop.ReportOnce();
        loop.ReportOnce();

        Assert.That(reporter.Sent, Has.Count.EqualTo(1));
    }

    [Test]
    public void ReportOnce_resends_the_same_snapshot_after_reenroll()
    {
        var (loop, reporter, _, hosts) = Create();
        hosts.NoteJoinStatus(200);
        loop.ReportOnce();

        hosts.NoteJoinStatus(202);
        loop.ReportOnce();
        hosts.NoteJoinStatus(200);
        loop.ReportOnce();

        Assert.That(reporter.Sent, Has.Count.EqualTo(2));
        Assert.That(reporter.Sent[1].Devices.Select(device => device.DeviceId), Does.Contain("d0"));
    }

    [Test]
    public void ReportOnce_sends_again_after_wake()
    {
        var (loop, reporter, devices, hosts) = Create();
        hosts.NoteJoinStatus(200);
        loop.ReportOnce();

        devices.Wake(new DeviceMutationRequest
        {
            DeviceId = "d0",
            OperationId = "op",
            ControllerIdentity = "control",
            Deadline = DateTimeOffset.UtcNow.AddMinutes(1),
            DesiredPowerState = DevicePowerState.Active
        });
        loop.ReportOnce();

        Assert.That(reporter.Sent, Has.Count.EqualTo(2));
        Assert.That(reporter.Sent[1].Devices.Single().PowerState, Is.EqualTo(DevicePowerState.Active));
    }

    private static (DevicesReportLoop Loop, RecordingReporter Reporter, DevicesService Devices, HostsController Hosts) Create()
    {
        var config = new NodeConfig
        {
            HostId = "dev",
            ControlEndpoint = new Uri("http://127.0.0.1:8080")
        };
        var runtime = new FakeOsdRuntime();
        runtime.Listed.Add(0);
        var osds = new OsdsService(runtime, config);
        osds.Seed(new ColdCeph.Core.Features.Osds.DTOs.OsdDto
        {
            OsdId = 0,
            HostId = "dev",
            DeviceId = "d0",
            Up = false,
            In = true,
            ProcessRunning = false
        });
        var devices = new DevicesService(new FakeDiskPower { State = DevicePowerState.Standby }, new OsdsController(osds), config);
        devices.Seed(new DeviceDto
        {
            DeviceId = "d0",
            HostId = "dev",
            MappedOsdId = 0,
            Wwn = "wwn",
            Serial = "s",
            Path = "/dev/sda",
            PowerState = DevicePowerState.Standby
        });
        var hosts = new HostsController(new HostsService(config));
        var reporter = new RecordingReporter();
        var loop = new DevicesReportLoop(new DevicesController(devices), hosts, reporter, config);
        return (loop, reporter, devices, hosts);
    }

    private sealed class RecordingReporter : IControlDevicesReporter
    {
        public List<HostDevicesObservationDto> Sent { get; } = [];

        public void Send(HostDevicesObservationDto observation) => Sent.Add(observation);
    }
}

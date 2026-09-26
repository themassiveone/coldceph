using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Devices.Controllers;
using ColdCeph.Control.Features.Devices.Services;
using ColdCeph.Control.Features.Hosts.Controllers;
using ColdCeph.Control.Features.Hosts.Services;
using ColdCeph.Control.Features.Osds.Controllers;
using ColdCeph.Control.Features.Osds.Services;
using ColdCeph.Control.Features.StoragePlane.Controllers;
using ColdCeph.Control.Features.StoragePlane.Services;
using ColdCeph.Control.Tests.Fake;
using ColdCeph.Core.Features.Devices.DTOs;
using ColdCeph.Core.Features.Hosts.DTOs;
using ColdCeph.Core.Features.Operations.DTOs;
using ColdCeph.Core.Features.Osds.DTOs;
using ColdCeph.Core.Features.StoragePlane.DTOs;

namespace ColdCeph.Control.Tests.Features.Devices.Unit;

[TestFixture]
public sealed class DevicesReconcilerTests
{
    [Test]
    public void Waking_issues_disk_wake_from_the_devices_slice()
    {
        var harness = Create(osdRunning: false);
        harness.Plane.RequestWake(OperationIdRules.Create().Value, "operator");

        harness.Reconciler.ReconcileOnce();

        Assert.That(harness.Node.Commands, Does.Contain("wake d0"));
    }

    [Test]
    public void Sleeping_does_not_standby_while_osds_still_run()
    {
        var harness = Create(osdRunning: true);
        var operationId = OperationIdRules.Create().Value;
        harness.Plane.RequestWake(operationId, "operator");
        harness.Plane.EnterReady(operationId);
        harness.Plane.RequestSleep(operationId, "operator");
        harness.Plane.EnterSleeping(operationId);

        harness.Reconciler.ReconcileOnce();

        Assert.That(harness.Node.Commands, Does.Not.Contain("standby d0"));
    }

    [Test]
    public void Node_failure_does_not_escape_reconcile()
    {
        var harness = Create(osdRunning: false);
        harness.Plane.RequestWake(OperationIdRules.Create().Value, "operator");
        harness.Node.ThrowOnList = true;

        Assert.DoesNotThrow(() => harness.Reconciler.ReconcileOnce());
        Assert.That(harness.Node.Commands, Is.Empty);
    }

    [Test]
    public void Node_failure_does_not_prevent_a_later_wake()
    {
        var harness = Create(osdRunning: false);
        harness.Plane.RequestWake(OperationIdRules.Create().Value, "operator");
        harness.Node.ThrowOnList = true;
        harness.Reconciler.ReconcileOnce();
        harness.Node.ThrowOnList = false;

        harness.Reconciler.ReconcileOnce();

        Assert.That(harness.Node.Commands, Does.Contain("wake d0"));
    }

    private static Harness Create(bool osdRunning)
    {
        var clock = new FakeClock();
        var config = new ControlConfig();
        var plane = new StoragePlaneService(new MemoryStoragePlaneRepository(), new RecordingNooutProvider(), clock, config);
        var osds = new OsdsService(new FakeNodeOsdsClient { Running = osdRunning }, config);
        osds.Seed(new OsdDto { OsdId = 0, HostId = "h1", DeviceId = "d0", Up = osdRunning, In = true, ProcessRunning = osdRunning });
        var node = new FakeNodeDevicesClient();
        var device = new DeviceDto
        {
            DeviceId = "d0",
            HostId = "h1",
            MappedOsdId = 0,
            Wwn = "wwn",
            Serial = "s",
            Path = "/dev/sda",
            PowerState = osdRunning ? DevicePowerState.Active : DevicePowerState.Standby
        };
        node.Inventory = [device];
        var devices = new DevicesService(node, config);
        devices.Seed(device);
        var hosts = new HostsService(config, clock);
        hosts.RegisterHeartbeat(new NodeStatusDto { HostId = "h1", Hostname = "h1", ObservedAt = clock.UtcNow }, new Uri("http://127.0.0.1:7080"));
        var reconciler = new DevicesReconciler(devices, new StoragePlaneController(plane), new OsdsController(osds), new HostsController(hosts));
        return new Harness(plane, reconciler, node);
    }

    private sealed record Harness(StoragePlaneService Plane, DevicesReconciler Reconciler, FakeNodeDevicesClient Node);
}

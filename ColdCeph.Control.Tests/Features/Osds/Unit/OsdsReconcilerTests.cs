using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Hosts.Controllers;
using ColdCeph.Control.Features.Hosts.Services;
using ColdCeph.Control.Features.Osds.Controllers;
using ColdCeph.Control.Features.Osds.Services;
using ColdCeph.Control.Features.StoragePlane.Controllers;
using ColdCeph.Control.Features.StoragePlane.Services;
using ColdCeph.Control.Tests.Fake;
using ColdCeph.Core.Features.Hosts.DTOs;
using ColdCeph.Core.Features.Operations.DTOs;
using ColdCeph.Core.Features.Osds.DTOs;
using ColdCeph.Core.Features.StoragePlane.DTOs;

namespace ColdCeph.Control.Tests.Features.Osds.Unit;

[TestFixture]
public sealed class OsdsReconcilerTests
{
    [Test]
    public void Waking_starts_osds_discovered_from_the_node()
    {
        var harness = Create(withHost: true);
        harness.Plane.RequestWake(OperationIdRules.Create().Value, "operator");

        harness.Reconciler.ReconcileOnce();

        Assert.That(harness.Node.Commands, Does.Contain("start 0"));
        Assert.That(harness.Osds.IsEveryProcessRunning(), Is.True);
    }

    [Test]
    public void Waking_does_not_start_osds_when_no_node_is_known()
    {
        var harness = Create(withHost: false);
        harness.Plane.RequestWake(OperationIdRules.Create().Value, "operator");

        harness.Reconciler.ReconcileOnce();

        Assert.That(harness.Node.Commands, Is.Empty);
        Assert.That(harness.Osds.IsEveryProcessRunning(), Is.False);
    }

    [Test]
    public void Node_failure_does_not_escape_reconcile()
    {
        var harness = Create(withHost: true);
        harness.Plane.RequestWake(OperationIdRules.Create().Value, "operator");
        harness.Node.ThrowOnList = true;

        Assert.DoesNotThrow(() => harness.Reconciler.ReconcileOnce());
        Assert.That(harness.Node.Commands, Is.Empty);
    }

    [Test]
    public void Node_failure_does_not_prevent_a_later_start()
    {
        var harness = Create(withHost: true);
        harness.Plane.RequestWake(OperationIdRules.Create().Value, "operator");
        harness.Node.ThrowOnList = true;
        harness.Reconciler.ReconcileOnce();
        harness.Node.ThrowOnList = false;

        harness.Reconciler.ReconcileOnce();

        Assert.That(harness.Node.Commands, Does.Contain("start 0"));
    }

    private static Harness Create(bool withHost)
    {
        var clock = new FakeClock();
        var config = new ControlConfig();
        var plane = new StoragePlaneService(new MemoryStoragePlaneRepository(), new RecordingNooutProvider(), clock, config);
        var node = new FakeNodeOsdsClient
        {
            Inventory =
            [
                new OsdDto { OsdId = 0, HostId = "h1", DeviceId = "d0", Up = false, In = true, ProcessRunning = false }
            ]
        };
        var osds = new OsdsService(node, config);
        var hosts = new HostsService(config, clock);
        if (withHost)
            hosts.RegisterHeartbeat(new NodeStatusDto { HostId = "h1", Hostname = "h1", ObservedAt = clock.UtcNow }, new Uri("http://127.0.0.1:7080"));
        var reconciler = new OsdsReconciler(osds, new StoragePlaneController(plane), new HostsController(hosts));
        return new Harness(plane, reconciler, node, osds);
    }

    private sealed record Harness(StoragePlaneService Plane, OsdsReconciler Reconciler, FakeNodeOsdsClient Node, OsdsService Osds);
}

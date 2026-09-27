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
    public void Waking_starts_osds_already_reported_by_the_node()
    {
        var harness = Create(withHost: true);
        harness.Plane.RequestWake(OperationIdRules.Create().Value, "operator");

        harness.Reconciler.ReconcileOnce();

        Assert.That(harness.Node.Commands, Does.Contain("7080 start 0"));
        Assert.That(harness.Osds.IsEveryProcessRunning(), Is.True);
    }

    [Test]
    public void Waking_starts_osds_on_every_enrolled_host()
    {
        var harness = CreateTwoHosts();
        harness.Plane.RequestWake(OperationIdRules.Create().Value, "operator");

        harness.Reconciler.ReconcileOnce();

        Assert.That(harness.Node.Commands, Does.Contain("7081 start 0"));
        Assert.That(harness.Node.Commands, Does.Contain("7082 start 1"));
        Assert.That(harness.Node.Commands, Does.Not.Contain("7081 start 1"));
        Assert.That(harness.Osds.ListOsds().Select(osd => osd.OsdId), Is.EquivalentTo(new[] { 0, 1 }));
    }

    [Test]
    public void Observed_osds_from_one_host_do_not_drop_the_other_host()
    {
        var harness = CreateTwoHosts();

        Assert.That(harness.Osds.GetOsd(0)?.HostId, Is.EqualTo("node-a"));
        Assert.That(harness.Osds.GetOsd(1)?.HostId, Is.EqualTo("node-b"));
    }

    [Test]
    public void One_host_failure_still_starts_osds_on_the_other()
    {
        var harness = CreateTwoHosts();
        harness.Plane.RequestWake(OperationIdRules.Create().Value, "operator");
        harness.Node.ThrowOnStartFor.Add(new Uri("http://127.0.0.1:7081"));

        harness.Reconciler.ReconcileOnce();

        Assert.That(harness.Node.Commands, Does.Contain("7082 start 1"));
        Assert.That(harness.Node.Commands, Does.Not.Contain("7081 start 0"));
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
        harness.Node.ThrowOnStart = true;

        Assert.DoesNotThrow(() => harness.Reconciler.ReconcileOnce());
        Assert.That(harness.Node.Commands, Is.Empty);
    }

    [Test]
    public void Node_failure_does_not_prevent_a_later_start()
    {
        var harness = Create(withHost: true);
        harness.Plane.RequestWake(OperationIdRules.Create().Value, "operator");
        harness.Node.ThrowOnStart = true;
        harness.Reconciler.ReconcileOnce();
        harness.Node.ThrowOnStart = false;

        harness.Reconciler.ReconcileOnce();

        Assert.That(harness.Node.Commands, Does.Contain("7080 start 0"));
    }

    private static Harness Create(bool withHost)
    {
        var clock = new FakeClock();
        var config = new ControlConfig();
        var plane = new StoragePlaneService(new MemoryStoragePlaneRepository(), new RecordingNooutProvider(), clock, config);
        var node = new FakeNodeOsdsClient();
        var osds = new OsdsService(node, config);
        if (withHost)
        {
            osds.ApplyObserved(new HostOsdsObservationDto
            {
                HostId = "h1",
                Osds =
                [
                    new OsdDto { OsdId = 0, HostId = "h1", DeviceId = "d0", Up = false, In = true, ProcessRunning = false }
                ]
            });
        }

        var hosts = new HostsService(config, clock);
        if (withHost)
            hosts.RegisterHeartbeat(new NodeStatusDto { HostId = "h1", Hostname = "h1", ObservedAt = clock.UtcNow }, new Uri("http://127.0.0.1:7080"));
        var reconciler = new OsdsReconciler(osds, new StoragePlaneController(plane), new HostsController(hosts));
        return new Harness(plane, reconciler, node, osds);
    }

    private static Harness CreateTwoHosts()
    {
        var clock = new FakeClock();
        var config = new ControlConfig();
        var plane = new StoragePlaneService(new MemoryStoragePlaneRepository(), new RecordingNooutProvider(), clock, config);
        var hostA = new Uri("http://127.0.0.1:7081");
        var hostB = new Uri("http://127.0.0.1:7082");
        var node = new FakeNodeOsdsClient();
        var osds = new OsdsService(node, config);
        osds.ApplyObserved(new HostOsdsObservationDto
        {
            HostId = "node-a",
            Osds = [new OsdDto { OsdId = 0, HostId = "node-a", DeviceId = "d0", Up = false, In = true, ProcessRunning = false }]
        });
        osds.ApplyObserved(new HostOsdsObservationDto
        {
            HostId = "node-b",
            Osds = [new OsdDto { OsdId = 1, HostId = "node-b", DeviceId = "d1", Up = false, In = true, ProcessRunning = false }]
        });
        var hosts = new HostsService(config, clock);
        hosts.RegisterHeartbeat(new NodeStatusDto { HostId = "node-a", Hostname = "node-a", ObservedAt = clock.UtcNow }, hostA);
        hosts.RegisterHeartbeat(new NodeStatusDto { HostId = "node-b", Hostname = "node-b", ObservedAt = clock.UtcNow }, hostB);
        var reconciler = new OsdsReconciler(osds, new StoragePlaneController(plane), new HostsController(hosts));
        return new Harness(plane, reconciler, node, osds);
    }

    private sealed record Harness(StoragePlaneService Plane, OsdsReconciler Reconciler, FakeNodeOsdsClient Node, OsdsService Osds);
}

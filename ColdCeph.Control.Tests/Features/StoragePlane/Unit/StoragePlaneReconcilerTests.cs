using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Devices.Controllers;
using ColdCeph.Control.Features.Devices.Services;
using ColdCeph.Control.Features.Hosts.Controllers;
using ColdCeph.Control.Features.Hosts.Services;
using ColdCeph.Control.Features.Integrity.Controllers;
using ColdCeph.Control.Features.Integrity.Services;
using ColdCeph.Control.Features.Osds.Controllers;
using ColdCeph.Control.Features.Osds.Services;
using ColdCeph.Control.Features.S3.Controllers;
using ColdCeph.Control.Features.S3.Repositories;
using ColdCeph.Control.Features.S3.Services;
using ColdCeph.Control.Features.StoragePlane.Controllers;
using ColdCeph.Control.Features.StoragePlane.Services;
using ColdCeph.Control.Tests.Fake;
using ColdCeph.Core.Features.Devices.DTOs;
using ColdCeph.Core.Features.Hosts.DTOs;
using ColdCeph.Core.Features.Operations.DTOs;
using ColdCeph.Core.Features.Osds.DTOs;
using ColdCeph.Core.Features.S3.DTOs;
using ColdCeph.Core.Features.StoragePlane.DTOs;

namespace ColdCeph.Control.Tests.Features.StoragePlane.Unit;

[TestFixture]
public sealed class StoragePlaneReconcilerTests
{
    [Test]
    public void Pending_s3_work_from_cold_starts_one_wake()
    {
        var harness = Create();
        harness.Ledger.BeginQueued();

        harness.Reconciler.ReconcileOnce();

        Assert.That(harness.Plane.GetState().State, Is.EqualTo(StoragePlaneState.Waking));
        harness.Ledger.BeginQueued();
        harness.Reconciler.ReconcileOnce();
        Assert.That(harness.Plane.GetLease()!.OperationId, Is.EqualTo(harness.Plane.GetState().ActiveOperationId));
        Assert.That(harness.Ceph.HealthDetailCalls, Is.EqualTo(0));
    }

    [Test]
    public void Reconcile_does_not_query_ceph()
    {
        var harness = Create();

        harness.Reconciler.ReconcileOnce();
        harness.Reconciler.ReconcileOnce();

        Assert.That(harness.Ceph.HealthDetailCalls, Is.EqualTo(0));
        Assert.That(harness.Ceph.MembershipCalls, Is.EqualTo(0));
    }

    [Test]
    public void Unexpected_integrity_does_not_fault_until_an_external_confirm()
    {
        var harness = Create();
        harness.Plane.MarkObserved(StoragePlaneState.Cold, "startup-reconcile");
        harness.Ceph.Health = new()
        {
            Status = "HEALTH_ERR",
            Summary = "unfound objects",
            Checks = ["unfound objects"]
        };
        harness.Ceph.HasUnfound = true;
        harness.Ceph.HealthChecks = ["unfound objects"];

        harness.Reconciler.ReconcileOnce();

        Assert.That(harness.Plane.GetState().State, Is.Not.EqualTo(StoragePlaneState.Faulted));
        Assert.That(harness.Ceph.HealthDetailCalls, Is.EqualTo(0));
    }

    [Test]
    public void Unexpected_integrity_faults_after_an_external_confirm()
    {
        var harness = Create();
        harness.Plane.MarkObserved(StoragePlaneState.Cold, "startup-reconcile");
        harness.Ceph.Health = new()
        {
            Status = "HEALTH_ERR",
            Summary = "unfound objects",
            Checks = ["unfound objects"]
        };
        harness.Ceph.HasUnfound = true;
        harness.Ceph.HealthChecks = ["unfound objects"];

        _ = harness.Integrity.GetIntegrity();
        var calls = harness.Ceph.HealthDetailCalls;
        harness.Reconciler.ReconcileOnce();

        Assert.That(harness.Plane.GetState().State, Is.EqualTo(StoragePlaneState.Faulted));
        Assert.That(harness.Ceph.HealthDetailCalls, Is.EqualTo(calls));
    }

    [Test]
    public void Waking_enters_ready_when_every_osd_process_is_running()
    {
        var harness = Create();
        harness.Plane.MarkObserved(StoragePlaneState.Cold, "startup-reconcile");
        harness.Osds.Seed(new OsdDto { OsdId = 0, HostId = "h1", DeviceId = "d0", Up = true, In = true, ProcessRunning = true });
        var operationId = OperationIdRules.Create().Value;
        harness.Plane.RequestWake(operationId, "operator");

        harness.Reconciler.ReconcileOnce();

        Assert.That(harness.Plane.GetState().State, Is.EqualTo(StoragePlaneState.Ready));
        Assert.That(harness.Ceph.HealthDetailCalls, Is.EqualTo(0));
    }

    [Test]
    public void Waking_stays_waking_when_an_osd_process_is_stopped()
    {
        var harness = Create();
        harness.Plane.MarkObserved(StoragePlaneState.Cold, "startup-reconcile");
        var operationId = OperationIdRules.Create().Value;
        harness.Plane.RequestWake(operationId, "operator");

        harness.Reconciler.ReconcileOnce();

        Assert.That(harness.Plane.GetState().State, Is.EqualTo(StoragePlaneState.Waking));
        Assert.That(harness.Ceph.HealthDetailCalls, Is.EqualTo(0));
    }

    [Test]
    public void Idle_sleep_uses_last_confirm_and_does_not_query_ceph()
    {
        var harness = Create();
        harness.Plane.MarkObserved(StoragePlaneState.Cold, "startup-reconcile");
        var operationId = OperationIdRules.Create().Value;
        harness.Plane.RequestWake(operationId, "operator");
        harness.Plane.EnterReady(operationId);
        harness.Clock.UtcNow += TimeSpan.FromMinutes(16);

        harness.Reconciler.ReconcileOnce();

        Assert.That(harness.Plane.GetState().State, Is.EqualTo(StoragePlaneState.Ready));
        Assert.That(harness.Ceph.HealthDetailCalls, Is.EqualTo(0));

        _ = harness.Integrity.GetIntegrity();
        var calls = harness.Ceph.HealthDetailCalls;
        harness.Reconciler.ReconcileOnce();

        Assert.That(harness.Plane.GetState().State, Is.EqualTo(StoragePlaneState.Quiescing));
        Assert.That(harness.Ceph.HealthDetailCalls, Is.EqualTo(calls));
    }

    [Test]
    public void Idle_does_not_sleep_when_last_confirm_is_not_sleep_safe()
    {
        var harness = Create();
        harness.Plane.MarkObserved(StoragePlaneState.Cold, "startup-reconcile");
        var operationId = OperationIdRules.Create().Value;
        harness.Plane.RequestWake(operationId, "operator");
        harness.Plane.EnterReady(operationId);
        harness.Ceph.PgsClean = false;
        harness.Ceph.HasRecoveryOrBackfill = true;
        _ = harness.Integrity.GetIntegrity();
        harness.Clock.UtcNow += TimeSpan.FromMinutes(16);

        harness.Reconciler.ReconcileOnce();

        Assert.That(harness.Plane.GetState().State, Is.EqualTo(StoragePlaneState.Ready));
        Assert.That(harness.Plane.GetState().State, Is.Not.EqualTo(StoragePlaneState.Quiescing));
    }

    [Test]
    public void Provider_failure_does_not_escape_reconcile()
    {
        var harness = Create();
        harness.Ceph.ThrowOnHealth = true;

        Assert.DoesNotThrow(() => harness.Reconciler.ReconcileOnce());
        Assert.That(harness.Plane.GetState().State, Is.EqualTo(StoragePlaneState.Cold));
        Assert.That(harness.Ceph.HealthDetailCalls, Is.EqualTo(0));
    }

    [Test]
    public void Provider_failure_does_not_prevent_a_later_successful_wake()
    {
        var harness = Create();
        harness.Ceph.ThrowOnHealth = true;
        harness.Reconciler.ReconcileOnce();
        harness.Ceph.ThrowOnHealth = false;
        harness.Ledger.BeginQueued();

        harness.Reconciler.ReconcileOnce();

        Assert.That(harness.Plane.GetState().State, Is.EqualTo(StoragePlaneState.Waking));
    }

    private static Harness Create()
    {
        var clock = new FakeClock();
        var config = new ControlConfig { BindHttpListeners = false, IdleTimeout = TimeSpan.FromMinutes(15) };
        var repo = new MemoryStoragePlaneRepository();
        var noout = new RecordingNooutProvider();
        var plane = new StoragePlaneService(repo, noout, clock, config);
        var planeController = new StoragePlaneController(plane);
        var ceph = new FakeCephQueryProvider();
        var integrity = new IntegrityService(ceph, new MemoryIntegrityRepository(), planeController, clock);
        var integrityController = new IntegrityController(integrity);
        var ledger = new MemoryRequestLedger(clock);
        var s3 = new S3Service(ledger, planeController, integrityController, new FakeRgwProxy(), config);
        var osds = new OsdsService(new FakeNodeOsdsClient(), config);
        osds.Seed(new OsdDto { OsdId = 0, HostId = "h1", DeviceId = "d0", Up = false, In = true, ProcessRunning = false });
        var devices = new DevicesService(new FakeNodeDevicesClient(), config);
        devices.Seed(new DeviceDto
        {
            DeviceId = "d0",
            HostId = "h1",
            MappedOsdId = 0,
            Wwn = "wwn",
            Serial = "s",
            Path = "/dev/sda",
            PowerState = DevicePowerState.Standby
        });
        var hosts = new HostsService(config, clock);
        hosts.RegisterHeartbeat(new NodeStatusDto { HostId = "h1", Hostname = "h1", ObservedAt = clock.UtcNow }, new Uri("http://127.0.0.1:7080"));
        var reconciler = new StoragePlaneReconciler(
            plane,
            new S3Controller(s3),
            integrityController,
            new OsdsController(osds, integrityController),
            new DevicesController(devices));
        return new Harness(plane, reconciler, ledger, ceph, integrityController, clock, osds);
    }

    private sealed record Harness(
        StoragePlaneService Plane,
        StoragePlaneReconciler Reconciler,
        MemoryRequestLedger Ledger,
        FakeCephQueryProvider Ceph,
        IntegrityController Integrity,
        FakeClock Clock,
        OsdsService Osds);
}

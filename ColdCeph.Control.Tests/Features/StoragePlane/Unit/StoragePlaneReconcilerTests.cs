using ColdCeph.Control.Tests.Support;
using ColdCeph.Core.Features.StoragePlane.DTOs;

namespace ColdCeph.Control.Tests.Features.StoragePlane.Unit;

[TestFixture]
public sealed class StoragePlaneReconcilerTests
{
    // ---- S3-triggered wake --------------------------------------------------

    [Test]
    public void Pending_s3_work_from_cold_starts_one_wake()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.Plane.MarkObserved(StoragePlaneState.Cold, "startup-reconcile");
        harness.Ledger.BeginQueued();

        harness.PlaneLoop.ReconcileOnce();

        Assert.That(harness.State, Is.EqualTo(StoragePlaneState.Waking));
    }

    [Test]
    public void Many_pending_s3_requests_still_hold_one_lease()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.Plane.MarkObserved(StoragePlaneState.Cold, "startup-reconcile");
        harness.Ledger.BeginQueued();
        harness.PlaneLoop.ReconcileOnce();
        var lease = harness.Plane.GetLease()!.OperationId;

        harness.Ledger.BeginQueued();
        harness.Ledger.BeginQueued();
        harness.PlaneLoop.ReconcileOnce();

        Assert.That(harness.Plane.GetLease()!.OperationId, Is.EqualTo(lease));
        Assert.That(harness.State, Is.EqualTo(StoragePlaneState.Waking));
    }

    [Test]
    public void No_pending_work_does_not_wake()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.Plane.MarkObserved(StoragePlaneState.Cold, "startup-reconcile");

        harness.PlaneLoop.ReconcileOnce();

        Assert.That(harness.State, Is.EqualTo(StoragePlaneState.Cold));
    }

    // ---- Control never polls Ceph ------------------------------------------

    [Test]
    public void Reconciling_never_confirms_ceph()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();

        harness.Tick(5);

        Assert.That(harness.Ceph.ObservationCalls, Is.EqualTo(0));
        Assert.That(harness.Ceph.MembershipCalls, Is.EqualTo(0));
    }

    // ---- FAULTED -----------------------------------------------------------

    [Test]
    public void A_durability_failure_does_not_fault_before_an_external_confirmation()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.Plane.MarkObserved(StoragePlaneState.Cold, "startup-reconcile");
        harness.Ceph.Seeing(CephFixture.Unfound);

        harness.Tick(3);

        Assert.That(harness.State, Is.Not.EqualTo(StoragePlaneState.Faulted));
        Assert.That(harness.Ceph.ObservationCalls, Is.EqualTo(0));
    }

    [Test]
    public void A_durability_failure_faults_after_an_external_confirmation()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.Plane.MarkObserved(StoragePlaneState.Cold, "startup-reconcile");
        harness.Ceph.Seeing(CephFixture.Unfound);
        harness.Confirm();
        var confirmations = harness.Ceph.ObservationCalls;

        harness.PlaneLoop.ReconcileOnce();

        Assert.That(harness.State, Is.EqualTo(StoragePlaneState.Faulted));
        Assert.That(harness.Ceph.ObservationCalls, Is.EqualTo(confirmations));
    }

    /// <summary>
    /// The three warnings the E2E harness used to mute. If these faulted the appliance, no demo
    /// or small cluster could ever run.
    /// </summary>
    [TestCase(CephFixture.DemoWarnings)]
    [TestCase(CephFixture.ColdOsdsDown)]
    [TestCase(CephFixture.WakingPeering)]
    [TestCase(CephFixture.NearFull)]
    [TestCase(CephFixture.Recovering)]
    public void Ordinary_conditions_do_not_fault_after_a_confirmation(string scenario)
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.Plane.MarkObserved(StoragePlaneState.Cold, "startup-reconcile");
        harness.Ceph.Seeing(scenario);
        harness.Confirm();

        harness.PlaneLoop.ReconcileOnce();

        Assert.That(harness.State, Is.EqualTo(StoragePlaneState.Cold));
    }

    [Test]
    public void Stopped_osds_while_ready_do_not_fault()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.Wake();
        harness.Plane.EnterReady(harness.Plane.GetState().ActiveOperationId!);
        harness.Ceph.Seeing(CephFixture.ColdOsdsDown);
        harness.Confirm();

        harness.PlaneLoop.ReconcileOnce();

        Assert.That(harness.State, Is.EqualTo(StoragePlaneState.Ready));
    }

    [Test]
    public void A_clean_confirmation_does_not_leave_faulted_on_its_own()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.Plane.MarkObserved(StoragePlaneState.Cold, "startup-reconcile");
        harness.Plane.EnterFaulted("unexpected-integrity");
        harness.Ceph.Seeing(CephFixture.Healthy);
        harness.Confirm();

        harness.Tick(3);

        Assert.That(harness.State, Is.EqualTo(StoragePlaneState.Faulted));
    }

    /// <summary>
    /// An unreachable monitor must not fault the appliance. FAULTED does not clear by itself, so
    /// a network blip would need an operator to come back.
    /// </summary>
    [Test]
    public void An_unreachable_monitor_does_not_fault()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.Plane.MarkObserved(StoragePlaneState.Cold, "startup-reconcile");
        harness.Ceph.ThrowOnObservation = true;
        harness.Confirm();

        harness.Tick(3);

        Assert.That(harness.State, Is.EqualTo(StoragePlaneState.Cold));
    }

    // ---- WAKING ends on node-reported processes ----------------------------

    [Test]
    public void Waking_enters_ready_once_every_osd_process_runs()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.Wake();

        harness.Tick(3);

        Assert.That(harness.Osds.IsEveryProcessRunning(), Is.True);
        Assert.That(harness.State, Is.EqualTo(StoragePlaneState.Ready));
        Assert.That(harness.Ceph.ObservationCalls, Is.EqualTo(0));
    }

    [Test]
    public void Waking_stays_waking_while_a_node_reports_the_process_stopped()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.NodeOsds.StartDoesNotTake = true;
        harness.Wake();

        harness.Tick(5);

        Assert.That(harness.Osds.IsEveryProcessRunning(), Is.False);
        Assert.That(harness.State, Is.EqualTo(StoragePlaneState.Waking));
    }

    [Test]
    public void Waking_stays_waking_with_no_inventory_at_all()
    {
        var harness = new PlaneHarness().WithHost();
        harness.Plane.MarkObserved(StoragePlaneState.Cold, "startup-reconcile");
        harness.Plane.RequestWake(harness.NewOperationId(), "operator");

        harness.Tick(3);

        Assert.That(harness.State, Is.EqualTo(StoragePlaneState.Waking));
    }

    // ---- sleep -------------------------------------------------------------

    [Test]
    public void Idle_sleep_reads_the_last_confirmation_and_does_not_confirm_again()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.Wake();
        harness.Tick(2);
        Assert.That(harness.State, Is.EqualTo(StoragePlaneState.Ready));

        harness.Confirm();
        var confirmations = harness.Ceph.ObservationCalls;
        harness.Clock.UtcNow += TimeSpan.FromMinutes(16);
        harness.PlaneLoop.ReconcileOnce();

        // Nothing is in flight, so there is nothing to quiesce and the tick carries straight
        // through to SLEEPING.
        Assert.That(harness.State, Is.EqualTo(StoragePlaneState.Sleeping));
        Assert.That(harness.Ceph.ObservationCalls, Is.EqualTo(confirmations));
    }

    /// <summary>
    /// QUIESCING exists to drain in-flight requests, so an active one has to hold it there.
    /// </summary>
    [Test]
    public void Quiescing_waits_for_an_in_flight_s3_request_before_sleeping()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.Wake();
        harness.Tick(2);
        harness.Confirm();
        harness.Clock.UtcNow += TimeSpan.FromMinutes(16);
        var inFlight = harness.Ledger.BeginQueued();
        harness.Ledger.Activate(inFlight);
        harness.Plane.RequestSleep(harness.Plane.GetState().ActiveOperationId!, "operator");

        harness.PlaneLoop.ReconcileOnce();

        Assert.That(harness.State, Is.EqualTo(StoragePlaneState.Quiescing));

        harness.Ledger.Complete(inFlight);
        harness.PlaneLoop.ReconcileOnce();

        Assert.That(harness.State, Is.EqualTo(StoragePlaneState.Sleeping));
    }

    [Test]
    public void Idle_does_not_sleep_before_any_confirmation()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.Wake();
        harness.Tick(2);
        harness.Clock.UtcNow += TimeSpan.FromMinutes(16);

        harness.PlaneLoop.ReconcileOnce();

        Assert.That(harness.State, Is.EqualTo(StoragePlaneState.Ready));
    }

    [TestCase(CephFixture.Recovering)]
    [TestCase(CephFixture.NearFull)]
    [TestCase(CephFixture.Unfound)]
    public void Idle_does_not_sleep_when_the_last_confirmation_is_not_sleep_safe(string scenario)
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.Wake();
        harness.Tick(2);
        harness.Ceph.Seeing(scenario);
        harness.Confirm();
        harness.Clock.UtcNow += TimeSpan.FromMinutes(16);

        harness.PlaneLoop.ReconcileOnce();

        Assert.That(harness.State, Is.Not.EqualTo(StoragePlaneState.Quiescing));
    }

    [Test]
    public void A_full_sleep_reaches_cold_with_disks_in_standby()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.Wake();
        harness.Tick(2);
        harness.Confirm();
        harness.Clock.UtcNow += TimeSpan.FromMinutes(16);

        harness.Tick(5);

        Assert.That(harness.State, Is.EqualTo(StoragePlaneState.Cold));
        Assert.That(harness.Osds.IsEveryProcessStopped(), Is.True);
        Assert.That(harness.Devices.IsEveryDeviceStandby(), Is.True);
    }

    [Test]
    public void Sleeping_does_not_reach_cold_while_a_disk_is_still_awake()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.NodeDevices.StandbyDoesNotTake = true;
        harness.Wake();
        harness.Tick(2);
        harness.Confirm();
        harness.Clock.UtcNow += TimeSpan.FromMinutes(16);

        harness.Tick(5);

        Assert.That(harness.Devices.IsEveryDeviceStandby(), Is.False);
        Assert.That(harness.State, Is.EqualTo(StoragePlaneState.Sleeping));
    }

    [Test]
    public void Sleeping_does_not_reach_cold_while_an_osd_is_still_running()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.NodeOsds.StopDoesNotTake = true;
        harness.Wake();
        harness.Tick(2);
        harness.Confirm();
        harness.Clock.UtcNow += TimeSpan.FromMinutes(16);

        harness.Tick(5);

        Assert.That(harness.Osds.IsEveryProcessStopped(), Is.False);
        Assert.That(harness.State, Is.EqualTo(StoragePlaneState.Sleeping));
    }

    // ---- resilience --------------------------------------------------------

    [Test]
    public void A_provider_failure_does_not_escape_a_tick()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.Ceph.ThrowOnObservation = true;

        Assert.DoesNotThrow(() => harness.Tick(2));
        Assert.That(harness.State, Is.EqualTo(StoragePlaneState.Cold));
    }

    /// <summary>
    /// A node failure is swallowed so the loop survives, which used to mean it left no trace at
    /// all: a plane stuck in WAKING with nothing anywhere saying why.
    /// </summary>
    [Test]
    public void An_unreachable_node_is_recorded_rather_than_discarded()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.NodeOsds.ThrowOnStart = true;
        harness.Wake();
        harness.DevicesLoop.ReconcileOnce();

        harness.OsdsLoop.ReconcileOnce();

        Assert.That(harness.OsdsLoop.LastError, Does.Contain("h1"));
        Assert.That(harness.OsdsLoop.ConsecutiveFailures, Is.EqualTo(1));
    }

    [Test]
    public void A_recovered_node_clears_the_recorded_failure()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.NodeOsds.ThrowOnStart = true;
        harness.Wake();
        harness.Tick(1);
        Assert.That(harness.OsdsLoop.LastError, Is.Not.Null);

        harness.NodeOsds.ThrowOnStart = false;
        harness.Tick(1);

        Assert.That(harness.OsdsLoop.LastError, Is.Null);
        Assert.That(harness.OsdsLoop.ConsecutiveFailures, Is.EqualTo(0));
    }

    [Test]
    public void A_healthy_tick_records_no_failure()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.Wake();

        harness.Tick(3);

        Assert.That(harness.PlaneLoop.LastError, Is.Null);
        Assert.That(harness.OsdsLoop.LastError, Is.Null);
        Assert.That(harness.DevicesLoop.LastError, Is.Null);
    }

    [Test]
    public void A_recovered_provider_lets_a_later_wake_through()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.Plane.MarkObserved(StoragePlaneState.Cold, "startup-reconcile");
        harness.Ceph.ThrowOnObservation = true;
        harness.Tick(2);

        harness.Ceph.ThrowOnObservation = false;
        harness.Ledger.BeginQueued();
        harness.PlaneLoop.ReconcileOnce();

        Assert.That(harness.State, Is.EqualTo(StoragePlaneState.Waking));
    }

    /// <summary>
    /// Guards used to be evaluated against one snapshot read at the top of the tick, so a
    /// transition decided halfway through was invisible to the guards after it and the sequence
    /// depended on which tick each change landed in.
    /// </summary>
    [Test]
    public void A_wake_and_the_readiness_that_follows_settle_within_two_ticks()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.Plane.MarkObserved(StoragePlaneState.Cold, "startup-reconcile");
        harness.Ledger.BeginQueued();

        harness.Tick(2);

        Assert.That(harness.State, Is.EqualTo(StoragePlaneState.Ready));
    }
}

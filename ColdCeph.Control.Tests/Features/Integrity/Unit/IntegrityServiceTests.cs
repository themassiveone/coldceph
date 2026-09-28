using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Integrity.Services;
using ColdCeph.Control.Features.StoragePlane.Controllers;
using ColdCeph.Control.Features.StoragePlane.Services;
using ColdCeph.Control.Tests.Fake;
using ColdCeph.Control.Tests.Support;
using ColdCeph.Core.Features.Integrity.DTOs;
using ColdCeph.Core.Features.Operations.DTOs;
using ColdCeph.Core.Features.StoragePlane.DTOs;

namespace ColdCeph.Control.Tests.Features.Integrity.Unit;

/// <summary>
/// Classification, driven from the fixture corpus so the strings being classified are the
/// strings Ceph emits. Health classification keys on check names; the StoragePlane state
/// decides whether a cold-phase condition is expected.
/// </summary>
[TestFixture]
public sealed class IntegrityServiceTests
{
    // ---- cold-phase conditions ---------------------------------------------

    [Test]
    public void Stopped_osds_while_cold_are_expected()
    {
        var (integrity, ceph, plane) = Create();
        ceph.Seeing(CephFixture.ColdOsdsDown);
        plane.MarkObserved(StoragePlaneState.Cold, "startup-reconcile");

        var snapshot = integrity.GetIntegrity();

        Assert.That(snapshot.Checks.Select(check => check.Name),
            Is.EquivalentTo(new[] { "OSD_DOWN", "OSD_HOST_DOWN", "PG_AVAILABILITY" }));
        Assert.That(snapshot.Checks.All(check => check.Classification == HealthClassification.ExpectedCold), Is.True);
        Assert.That(snapshot.DurabilityFailure, Is.False);
    }

    [Test]
    public void Stopped_osds_while_ready_are_unexpected_and_hold_writes()
    {
        var (integrity, ceph, plane) = Create();
        ceph.Seeing(CephFixture.ColdOsdsDown);
        var operationId = OperationIdRules.Create().Value;
        plane.RequestWake(operationId, "operator");
        plane.EnterReady(operationId);

        var snapshot = integrity.GetIntegrity();

        Assert.That(snapshot.Checks.Any(check => check.Classification == HealthClassification.Unexpected), Is.True);
        Assert.That(snapshot.Predicates.WriteReady, Is.False);
        Assert.That(snapshot.DurabilityFailure, Is.False);
    }

    [Test]
    public void Peering_pgs_while_waking_are_expected_and_do_not_fault()
    {
        var (integrity, ceph, plane) = Create();
        ceph.Seeing(CephFixture.WakingPeering);
        plane.RequestWake(OperationIdRules.Create().Value, "operator");

        var snapshot = integrity.GetIntegrity();

        Assert.That(plane.GetState().State, Is.EqualTo(StoragePlaneState.Waking));
        Assert.That(snapshot.Checks.All(check => check.Classification == HealthClassification.ExpectedCold), Is.True);
        Assert.That(snapshot.DurabilityFailure, Is.False);
        Assert.That(snapshot.Predicates.ReadReady, Is.False);
    }

    /// <summary>
    /// The three warnings the E2E harness used to mute before any test ran. They are ordinary
    /// on a small cluster and must not hold the plane back in any state.
    /// </summary>
    [TestCase(StoragePlaneState.Cold)]
    [TestCase(StoragePlaneState.Ready)]
    public void Demo_cluster_warnings_are_expected_in_every_state(StoragePlaneState state)
    {
        var (integrity, ceph, plane) = Create();
        ceph.Seeing(CephFixture.DemoWarnings);
        Reach(plane, state);

        var snapshot = integrity.GetIntegrity();

        Assert.That(snapshot.Checks.Select(check => check.Name),
            Is.EquivalentTo(new[] { "POOL_NO_REDUNDANCY", "TOO_FEW_OSDS", "AUTH_INSECURE_GLOBAL_ID_RECLAIM_ALLOWED" }));
        Assert.That(snapshot.Checks.All(check => check.Classification == HealthClassification.ExpectedCold), Is.True);
        Assert.That(snapshot.DurabilityFailure, Is.False);
    }

    [Test]
    public void Demo_cluster_warnings_still_allow_writes()
    {
        var (integrity, ceph, plane) = Create();
        ceph.Seeing(CephFixture.DemoWarnings);
        Reach(plane, StoragePlaneState.Ready);

        Assert.That(integrity.GetIntegrity().Predicates.WriteReady, Is.True);
    }

    // ---- durability --------------------------------------------------------

    [TestCase(CephFixture.Unfound)]
    [TestCase(CephFixture.Inconsistent)]
    [TestCase(CephFixture.Incomplete)]
    public void Durability_failures_are_unexpected_even_while_cold(string scenario)
    {
        var (integrity, ceph, plane) = Create();
        ceph.Seeing(scenario);
        plane.MarkObserved(StoragePlaneState.Cold, "startup-reconcile");

        var snapshot = integrity.GetIntegrity();

        Assert.That(snapshot.DurabilityFailure, Is.True);
        Assert.That(snapshot.Predicates.WriteReady, Is.False);
        Assert.That(snapshot.Predicates.SleepSafe, Is.False);
    }

    [TestCase(CephFixture.Unfound)]
    [TestCase(CephFixture.Inconsistent)]
    public void Durability_failures_are_unexpected_while_waking(string scenario)
    {
        var (integrity, ceph, plane) = Create();
        ceph.Seeing(scenario);
        plane.RequestWake(OperationIdRules.Create().Value, "operator");

        var snapshot = integrity.GetIntegrity();

        Assert.That(plane.GetState().State, Is.EqualTo(StoragePlaneState.Waking));
        Assert.That(snapshot.DurabilityFailure, Is.True);
    }

    /// <summary>
    /// Only the damage checks are marked durability, not every check in the confirmation.
    /// The unfound fixture also carries an ordinary <c>PG_DEGRADED</c>.
    /// </summary>
    [Test]
    public void Only_the_damage_check_is_marked_durability()
    {
        var (integrity, ceph, _) = Create();
        ceph.Seeing(CephFixture.Unfound);

        var snapshot = integrity.GetIntegrity();

        Assert.That(snapshot.Checks.Where(check => check.Durability).Select(check => check.Name),
            Is.EquivalentTo(new[] { "OBJECT_UNFOUND" }));
        Assert.That(snapshot.Checks.Select(check => check.Name), Does.Contain("PG_DEGRADED"));
    }

    // ---- controller-owned noout --------------------------------------------

    /// <summary>
    /// A scoped <c>noout</c> ColdCeph set itself is expected. It is expected because
    /// StoragePlane holds the record, not because the check has a particular name — Ceph reports
    /// a group flag under <c>OSD_FLAGS</c> and a cluster-wide one under <c>OSDMAP_FLAGS</c>.
    /// </summary>
    [TestCase(CephFixture.ScopedNoout)]
    [TestCase(CephFixture.ClusterNoout)]
    public void Noout_is_expected_when_storage_plane_owns_the_flag(string scenario)
    {
        var (integrity, ceph, plane) = Create();
        ceph.Seeing(scenario);
        ReachSleeping(plane);

        var snapshot = integrity.GetIntegrity();

        Assert.That(plane.ListOwnedNoout().Select(record => record.Scope), Does.Contain("hdd-osds"));
        Assert.That(snapshot.Checks.All(check => check.Classification == HealthClassification.ExpectedCold), Is.True);
        Assert.That(snapshot.Predicates.SleepSafe, Is.True);
    }

    /// <summary>
    /// The same flag with no controller-owned record is somebody else's, and the invariant is
    /// that ColdCeph only clears what it recorded. It must not be normalised away.
    /// </summary>
    [TestCase(CephFixture.ScopedNoout)]
    [TestCase(CephFixture.ClusterNoout)]
    public void Noout_is_unexpected_when_storage_plane_owns_nothing(string scenario)
    {
        var (integrity, ceph, plane) = Create();
        ceph.Seeing(scenario);
        Reach(plane, StoragePlaneState.Cold);

        var snapshot = integrity.GetIntegrity();

        Assert.That(plane.ListOwnedNoout(), Is.Empty);
        Assert.That(snapshot.Checks.Any(check => check.Classification == HealthClassification.Unexpected), Is.True);
        Assert.That(snapshot.Predicates.WriteReady, Is.False);
    }

    [Test]
    public void A_flag_other_than_noout_stays_unexpected_even_when_noout_is_owned()
    {
        var (integrity, ceph, plane) = Create();
        ceph.Seeing(CephFixture.OtherFlag);
        ReachSleeping(plane);

        var snapshot = integrity.GetIntegrity();

        Assert.That(snapshot.Checks.Single().Detail, Does.Contain("noup"));
        Assert.That(snapshot.Checks.Any(check => check.Classification == HealthClassification.Unexpected), Is.True);
    }

    // ---- readiness ---------------------------------------------------------

    [Test]
    public void A_healthy_ready_cluster_is_write_ready_and_sleep_safe()
    {
        var (integrity, ceph, plane) = Create();
        ceph.Seeing(CephFixture.Healthy);
        Reach(plane, StoragePlaneState.Ready);

        var snapshot = integrity.GetIntegrity();

        Assert.That(snapshot.Predicates.WriteReady, Is.True);
        Assert.That(snapshot.Predicates.SleepSafe, Is.True);
    }

    /// <summary>
    /// Recovery means reduced redundancy, not unreadable data, so reads keep flowing while
    /// writes and sleep wait. Classifying PG_DEGRADED as unexpected closed reads for the whole
    /// of every recovery window.
    /// </summary>
    [Test]
    public void Recovery_keeps_reads_open()
    {
        var (integrity, ceph, plane) = Create();
        ceph.Seeing(CephFixture.Recovering);
        Reach(plane, StoragePlaneState.Ready);

        var snapshot = integrity.GetIntegrity();

        Assert.That(snapshot.Checks.All(check => check.Classification == HealthClassification.ExpectedCold), Is.True);
        Assert.That(snapshot.Predicates.ReadReady, Is.True);
    }

    /// <summary>
    /// Unavailable data is the opposite case: outside a cold phase it closes reads.
    /// </summary>
    [Test]
    public void Unavailable_pgs_while_ready_close_reads()
    {
        var (integrity, ceph, plane) = Create();
        ceph.Seeing(CephFixture.ColdOsdsDown);
        Reach(plane, StoragePlaneState.Ready);

        var snapshot = integrity.GetIntegrity();

        Assert.That(snapshot.Checks.Select(check => check.Name), Does.Contain("PG_AVAILABILITY"));
        Assert.That(snapshot.Predicates.ReadReady, Is.False);
    }

    [Test]
    public void Recovery_holds_writes_and_refuses_sleep()
    {
        var (integrity, ceph, plane) = Create();
        ceph.Seeing(CephFixture.Recovering);
        Reach(plane, StoragePlaneState.Ready);

        var snapshot = integrity.GetIntegrity();

        Assert.That(snapshot.Predicates.WriteReady, Is.False);
        Assert.That(snapshot.Predicates.SleepSafe, Is.False);
    }

    [Test]
    public void Losing_quorum_closes_reads_and_writes()
    {
        var (integrity, ceph, plane) = Create();
        ceph.Seeing(CephFixture.NoQuorum);
        Reach(plane, StoragePlaneState.Ready);

        var snapshot = integrity.GetIntegrity();

        Assert.That(snapshot.Predicates.ControlPlaneAvailable, Is.False);
        Assert.That(snapshot.Predicates.ReadReady, Is.False);
        Assert.That(snapshot.Predicates.WriteReady, Is.False);
    }

    // ---- one confirmation per request --------------------------------------

    [Test]
    public void One_get_is_one_confirmation()
    {
        var (integrity, ceph, _) = Create();

        _ = integrity.GetIntegrity();

        Assert.That(ceph.ObservationCalls, Is.EqualTo(1));
    }

    [Test]
    public void Reading_the_last_snapshot_does_not_confirm_again()
    {
        var (integrity, ceph, _) = Create();
        _ = integrity.GetIntegrity();

        _ = integrity.GetLastIntegrity();
        _ = integrity.GetRawHealth();

        Assert.That(ceph.ObservationCalls, Is.EqualTo(1));
    }

    [Test]
    public void The_last_snapshot_before_any_confirmation_is_unavailable_and_reaches_no_further()
    {
        var (integrity, ceph, _) = Create();
        ceph.Seeing(CephFixture.Unfound);

        var snapshot = integrity.GetLastIntegrity();

        Assert.That(ceph.ObservationCalls, Is.EqualTo(0));
        Assert.That(snapshot.Raw.Status, Is.EqualTo("UNAVAILABLE"));
        Assert.That(snapshot.Raw.Status, Is.Not.EqualTo("HEALTH_ERR"));
        Assert.That(snapshot.DurabilityFailure, Is.False);
    }

    [Test]
    public void The_last_snapshot_after_a_confirmation_is_that_confirmation()
    {
        var (integrity, ceph, _) = Create();
        ceph.Seeing(CephFixture.NearFull);

        _ = integrity.GetIntegrity();
        var snapshot = integrity.GetLastIntegrity();

        Assert.That(snapshot.Checks.Select(check => check.Name), Does.Contain("OSD_NEARFULL"));
        Assert.That(ceph.ObservationCalls, Is.EqualTo(1));
    }

    // ---- failure -----------------------------------------------------------

    [Test]
    public void An_unreachable_monitor_is_unavailable_and_grants_nothing()
    {
        var (integrity, ceph, _) = Create();
        ceph.ThrowOnObservation = true;

        var snapshot = integrity.GetIntegrity();

        Assert.That(snapshot.Raw.Status, Is.EqualTo("UNAVAILABLE"));
        Assert.That(snapshot.Predicates.ReadReady, Is.False);
        Assert.That(snapshot.Predicates.WriteReady, Is.False);
        Assert.That(snapshot.Predicates.SleepSafe, Is.False);
    }

    /// <summary>
    /// An unreachable monitor must not read as a durability failure: that would FAULT the
    /// appliance for a network problem, and FAULTED does not clear on its own.
    /// </summary>
    [Test]
    public void An_unreachable_monitor_is_not_a_durability_failure()
    {
        var (integrity, ceph, _) = Create();
        ceph.ThrowOnObservation = true;

        Assert.That(integrity.GetIntegrity().DurabilityFailure, Is.False);
    }

    [Test]
    public void A_reachable_monitor_is_not_unavailable()
    {
        var (integrity, _, _) = Create();

        Assert.That(integrity.GetIntegrity().Raw.Status, Is.EqualTo("HEALTH_OK"));
    }

    [Test]
    public void Capacity_comes_from_the_same_confirmation()
    {
        var (integrity, _, _) = Create();

        var snapshot = integrity.GetIntegrity();

        Assert.That(snapshot.Capacity?.TotalBytes, Is.EqualTo(3_000_000_000));
        Assert.That(snapshot.CapacityUnavailableReason, Is.Null);
    }

    [Test]
    public void A_capacity_failure_does_not_hide_a_successful_confirmation()
    {
        var (integrity, ceph, _) = Create();
        ceph.Observation = ceph.Observation with
        {
            Capacity = null,
            CapacityUnavailableReason = "capacity unavailable"
        };

        var snapshot = integrity.GetIntegrity();

        Assert.That(snapshot.Raw.Status, Is.EqualTo("HEALTH_OK"));
        Assert.That(snapshot.Capacity, Is.Null);
        Assert.That(snapshot.CapacityUnavailableReason, Is.EqualTo("capacity unavailable"));
    }

    // ---- osd membership overlay --------------------------------------------

    [Test]
    public void Membership_returns_the_ceph_map_rows()
    {
        var (integrity, ceph, _) = Create();
        ceph.OsdMembership = new Dictionary<int, OsdMembershipDto>
        {
            [0] = new() { OsdId = 0, Up = true, In = true }
        };

        var membership = integrity.ListOsdMembership(out var reason);

        Assert.That(membership[0].Up, Is.True);
        Assert.That(reason, Is.Null);
    }

    /// <summary>
    /// An unreachable monitor and a cluster with no OSDs both yield an empty overlay, so the
    /// reason distinguishes them. Without it the operator cannot tell "Ceph did not answer"
    /// from "there are no OSDs".
    /// </summary>
    [Test]
    public void An_unreachable_monitor_says_why_the_overlay_is_empty()
    {
        var (integrity, ceph, _) = Create();
        ceph.ThrowOnMembership = true;

        var membership = integrity.ListOsdMembership(out var reason);

        Assert.That(membership, Is.Empty);
        Assert.That(reason, Is.EqualTo("ceph unavailable"));
    }

    [Test]
    public void A_cluster_with_no_osds_gives_an_empty_overlay_and_no_reason()
    {
        var (integrity, _, _) = Create();

        var membership = integrity.ListOsdMembership(out var reason);

        Assert.That(membership, Is.Empty);
        Assert.That(reason, Is.Null);
    }

    /// <summary>
    /// Drives the plane to SLEEPING, which is where StoragePlane actually takes ownership of
    /// the scoped noout flag.
    /// </summary>
    private static void ReachSleeping(StoragePlaneService plane)
    {
        var operationId = OperationIdRules.Create().Value;
        plane.MarkObserved(StoragePlaneState.Cold, "startup-reconcile");
        plane.RequestWake(operationId, "operator");
        plane.EnterReady(operationId);
        plane.RequestSleep(operationId, "operator");
        plane.EnterSleeping(operationId);
    }

    private static void Reach(StoragePlaneService plane, StoragePlaneState state)
    {
        if (state == StoragePlaneState.Cold)
        {
            plane.MarkObserved(StoragePlaneState.Cold, "startup-reconcile");
            return;
        }

        var operationId = OperationIdRules.Create().Value;
        plane.RequestWake(operationId, "operator");
        if (state == StoragePlaneState.Ready)
            plane.EnterReady(operationId);
    }

    private static (IntegrityService Integrity, FakeCephQueryProvider Ceph, StoragePlaneService Plane) Create()
    {
        var clock = new FakeClock();
        var plane = new StoragePlaneService(
            new MemoryStoragePlaneRepository(), new RecordingNooutProvider(), clock, new ControlConfig());
        var ceph = new FakeCephQueryProvider();
        var integrity = new IntegrityService(
            ceph, new MemoryIntegrityRepository(), new StoragePlaneController(plane), clock);
        return (integrity, ceph, plane);
    }
}

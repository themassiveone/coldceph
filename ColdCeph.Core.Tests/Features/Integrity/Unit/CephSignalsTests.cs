using ColdCeph.Core.Features.Integrity.DTOs;
using ColdCeph.Core.Tests.Support;

namespace ColdCeph.Core.Tests.Features.Integrity.Unit;

/// <summary>
/// The readiness arithmetic, as a pure function of one confirmation. Every predicate has a
/// case that turns it on and a case that turns it off; anything else could not tell a correct
/// implementation from one that returns a constant.
/// </summary>
[TestFixture]
public sealed class CephSignalsTests
{
    // ---- durability ---------------------------------------------------------

    [Test]
    public void Unfound_objects_are_a_durability_failure()
    {
        var signals = CephSignals.From(CephObservations.With(
            status: "HEALTH_ERR",
            checkNames: ["OBJECT_UNFOUND"],
            pgStates: ["active+recovery_unfound+degraded"]));

        Assert.That(signals.HasUnfound, Is.True);
        Assert.That(signals.DurabilityFailure, Is.True);
    }

    [Test]
    public void Unfound_pg_state_alone_is_a_durability_failure()
    {
        var signals = CephSignals.From(CephObservations.With(pgStates: ["active+backfill_unfound"]));

        Assert.That(signals.HasUnfound, Is.True);
    }

    [TestCase("PG_DAMAGED")]
    [TestCase("OSD_SCRUB_ERRORS")]
    public void Damage_checks_are_durability_failures(string name)
    {
        var signals = CephSignals.From(CephObservations.With(status: "HEALTH_ERR", checkNames: [name]));

        Assert.That(signals.HasInconsistent, Is.True);
        Assert.That(signals.DurabilityFailure, Is.True);
    }

    [Test]
    public void Inconsistent_pg_state_alone_is_a_durability_failure()
    {
        var signals = CephSignals.From(CephObservations.With(pgStates: ["active+clean+inconsistent"]));

        Assert.That(signals.HasInconsistent, Is.True);
    }

    [Test]
    public void Incomplete_pgs_are_a_durability_failure()
    {
        var signals = CephSignals.From(CephObservations.With(pgStates: ["incomplete"]));

        Assert.That(signals.HasIncomplete, Is.True);
        Assert.That(signals.DurabilityFailure, Is.True);
    }

    [TestCase("OSD_DOWN")]
    [TestCase("PG_AVAILABILITY")]
    [TestCase("PG_DEGRADED")]
    [TestCase("TOO_FEW_PGS")]
    [TestCase("OSD_NEARFULL")]
    [TestCase("POOL_NO_REDUNDANCY")]
    public void Ordinary_checks_are_not_durability_failures(string name)
    {
        var signals = CephSignals.From(CephObservations.With(
            status: "HEALTH_WARN",
            checkNames: [name],
            pgStates: ["stale+active+clean"]));

        Assert.That(signals.DurabilityFailure, Is.False);
    }

    [Test]
    public void A_clean_confirmation_is_not_a_durability_failure()
    {
        Assert.That(CephSignals.From(CephObservations.With()).DurabilityFailure, Is.False);
    }

    // ---- PG state -----------------------------------------------------------

    [Test]
    public void All_clean_pgs_are_active_and_clean()
    {
        var signals = CephSignals.From(CephObservations.With(pgStates: ["active+clean"]));

        Assert.That(signals.PgsActive, Is.True);
        Assert.That(signals.PgsClean, Is.True);
    }

    [Test]
    public void One_unclean_group_among_many_makes_the_cluster_unclean()
    {
        var signals = CephSignals.From(CephObservations.With(
            pgStates: ["active+clean", "active+undersized+degraded"]));

        Assert.That(signals.PgsActive, Is.True);
        Assert.That(signals.PgsClean, Is.False);
    }

    [Test]
    public void One_inactive_group_among_many_makes_the_cluster_inactive()
    {
        var signals = CephSignals.From(CephObservations.With(pgStates: ["active+clean", "peering"]));

        Assert.That(signals.PgsActive, Is.False);
    }

    /// <summary>
    /// <c>All</c> over an empty set is true, so without an explicit guard a confirmation that
    /// reported no PGs at all would read as active and clean and gate reads open.
    /// </summary>
    [Test]
    public void A_confirmation_with_no_pgs_is_neither_active_nor_clean()
    {
        var signals = CephSignals.From(CephObservations.With(pgStates: []));

        Assert.That(signals.PgsActive, Is.False);
        Assert.That(signals.PgsClean, Is.False);
    }

    [TestCase("stale+active+clean")]
    [TestCase("down+peering")]
    public void Stale_and_down_pgs_are_reported(string state)
    {
        Assert.That(CephSignals.From(CephObservations.With(pgStates: [state])).HasStaleOrDown, Is.True);
    }

    [Test]
    public void Clean_pgs_are_not_stale_or_down()
    {
        Assert.That(CephSignals.From(CephObservations.With()).HasStaleOrDown, Is.False);
    }

    [TestCase("active+recovering+degraded")]
    [TestCase("active+recovery_wait+degraded")]
    [TestCase("active+remapped+backfilling")]
    [TestCase("active+remapped+backfill_wait")]
    [TestCase("active+remapped+backfill_toofull")]
    public void Recovery_and_backfill_states_are_reported(string state)
    {
        Assert.That(CephSignals.From(CephObservations.With(pgStates: [state])).HasRecoveryOrBackfill, Is.True);
    }

    /// <summary>
    /// A cold appliance's PGs are stale, not recovering. Reporting recovery here would refuse
    /// sleep forever.
    /// </summary>
    [TestCase("active+clean")]
    [TestCase("stale+active+clean")]
    [TestCase("active+clean+scrubbing+deep")]
    public void Settled_states_are_not_recovery_or_backfill(string state)
    {
        Assert.That(CephSignals.From(CephObservations.With(pgStates: [state])).HasRecoveryOrBackfill, Is.False);
    }

    [Test]
    public void Pg_state_tokens_are_matched_whole_not_as_substrings()
    {
        // "deep" must not satisfy a search for "degraded", and "scrubbing" must not
        // satisfy one for "recovering".
        var signals = CephSignals.From(CephObservations.With(pgStates: ["active+clean+scrubbing+deep"]));

        Assert.That(signals.PgsClean, Is.True);
        Assert.That(signals.HasRecoveryOrBackfill, Is.False);
        Assert.That(signals.DurabilityFailure, Is.False);
    }

    // ---- full ---------------------------------------------------------------

    [TestCase("OSD_FULL")]
    [TestCase("OSD_NEARFULL")]
    [TestCase("OSD_BACKFILLFULL")]
    [TestCase("POOL_FULL")]
    [TestCase("POOL_NEAR_FULL")]
    [TestCase("PG_BACKFILL_FULL")]
    public void Full_checks_are_reported(string name)
    {
        Assert.That(CephSignals.From(CephObservations.With(checkNames: [name])).HasFullOsds, Is.True);
    }

    /// <summary>
    /// The old provider matched the substring "full" against message text, so any check whose
    /// prose happened to contain the word read as a full OSD.
    /// </summary>
    [Test]
    public void A_check_merely_mentioning_full_in_its_message_is_not_a_full_osd()
    {
        var signals = CephSignals.From(CephObservations.With(
            checkNames: ["PG_NOT_DEEP_SCRUBBED"],
            checkMessage: "1 pgs not deep-scrubbed since the pool was full"));

        Assert.That(signals.HasFullOsds, Is.False);
    }

    // ---- quorum -------------------------------------------------------------

    [Test]
    public void Quorum_is_carried_through_both_ways()
    {
        Assert.That(CephSignals.From(CephObservations.With(quorum: true)).QuorumAvailable, Is.True);
        Assert.That(CephSignals.From(CephObservations.With(quorum: false)).QuorumAvailable, Is.False);
    }

    // ---- readiness arithmetic ----------------------------------------------

    [Test]
    public void A_clean_confirmation_is_read_write_and_sleep_ready()
    {
        var predicates = CephSignals.From(CephObservations.With()).ToPredicates(osdPlaneExpected: true);

        Assert.That(predicates.ControlPlaneAvailable, Is.True);
        Assert.That(predicates.ReadReady, Is.True);
        Assert.That(predicates.WriteReady, Is.True);
        Assert.That(predicates.SleepSafe, Is.True);
    }

    [Test]
    public void Losing_quorum_closes_everything()
    {
        var predicates = CephSignals.From(CephObservations.With(quorum: false)).ToPredicates(osdPlaneExpected: true);

        Assert.That(predicates.ControlPlaneAvailable, Is.False);
        Assert.That(predicates.ReadReady, Is.False);
        Assert.That(predicates.WriteReady, Is.False);
        Assert.That(predicates.SleepSafe, Is.False);
    }

    [Test]
    public void An_unexpected_osd_plane_closes_reads_and_writes()
    {
        var predicates = CephSignals.From(CephObservations.With()).ToPredicates(osdPlaneExpected: false);

        Assert.That(predicates.OsdPlaneExpected, Is.False);
        Assert.That(predicates.ReadReady, Is.False);
        Assert.That(predicates.WriteReady, Is.False);
    }

    [Test]
    public void Recovery_holds_writes_and_sleep_but_not_reads()
    {
        var predicates = CephSignals
            .From(CephObservations.With(pgStates: ["active+recovering+degraded"]))
            .ToPredicates(osdPlaneExpected: true);

        Assert.That(predicates.ReadReady, Is.True);
        Assert.That(predicates.WriteReady, Is.False);
        Assert.That(predicates.SleepSafe, Is.False);
    }

    [Test]
    public void Nearfull_holds_writes_and_sleep()
    {
        var predicates = CephSignals
            .From(CephObservations.With(checkNames: ["OSD_NEARFULL"]))
            .ToPredicates(osdPlaneExpected: true);

        Assert.That(predicates.ReadReady, Is.True);
        Assert.That(predicates.WriteReady, Is.False);
        Assert.That(predicates.SleepSafe, Is.False);
    }

    [Test]
    public void Unfound_objects_close_reads_writes_and_sleep()
    {
        var predicates = CephSignals
            .From(CephObservations.With(checkNames: ["OBJECT_UNFOUND"]))
            .ToPredicates(osdPlaneExpected: true);

        Assert.That(predicates.ReadReady, Is.False);
        Assert.That(predicates.WriteReady, Is.False);
        Assert.That(predicates.SleepSafe, Is.False);
    }

    [Test]
    public void Incomplete_pgs_close_reads_writes_and_sleep()
    {
        var predicates = CephSignals
            .From(CephObservations.With(pgStates: ["incomplete"]))
            .ToPredicates(osdPlaneExpected: true);

        Assert.That(predicates.ReadReady, Is.False);
        Assert.That(predicates.WriteReady, Is.False);
        Assert.That(predicates.SleepSafe, Is.False);
    }

    [Test]
    public void Stale_pgs_close_reads_and_writes()
    {
        var predicates = CephSignals
            .From(CephObservations.With(pgStates: ["stale+active+clean"]))
            .ToPredicates(osdPlaneExpected: true);

        Assert.That(predicates.ReadReady, Is.False);
        Assert.That(predicates.WriteReady, Is.False);
    }

    /// <summary>
    /// Sleeping asks whether the cluster has settled, not whether it is serving, so a cold
    /// plane whose PGs are stale-but-clean is still safe to leave asleep.
    /// </summary>
    [Test]
    public void A_cold_plane_with_clean_stale_pgs_is_still_sleep_safe()
    {
        var predicates = CephSignals
            .From(CephObservations.With(status: "HEALTH_WARN", checkNames: ["OSD_DOWN"], pgStates: ["stale+active+clean"]))
            .ToPredicates(osdPlaneExpected: true);

        Assert.That(predicates.SleepSafe, Is.True);
        Assert.That(predicates.ReadReady, Is.False);
    }

    [Test]
    public void An_unavailable_confirmation_grants_nothing()
    {
        var predicates = CephSignals.Unavailable.ToPredicates(osdPlaneExpected: true);

        Assert.That(predicates.ControlPlaneAvailable, Is.False);
        Assert.That(predicates.ReadReady, Is.False);
        Assert.That(predicates.WriteReady, Is.False);
        Assert.That(predicates.SleepSafe, Is.False);
    }

    [Test]
    public void Unavailable_observation_derives_the_unavailable_signals()
    {
        var signals = CephSignals.From(CephObservation.Unavailable("ceph is unreachable"));

        Assert.That(signals.QuorumAvailable, Is.False);
        Assert.That(signals.PgsActive, Is.False);
        Assert.That(signals.DurabilityFailure, Is.False);
    }
}

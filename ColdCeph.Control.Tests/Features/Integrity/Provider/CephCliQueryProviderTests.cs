using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Integrity.Providers;
using ColdCeph.Control.Tests.Support;
using ColdCeph.Core.Features.Integrity.DTOs;

namespace ColdCeph.Control.Tests.Features.Integrity.Provider;

/// <summary>
/// Drives the provider from <c>Support/CephFixtures</c>, so every assertion here is about what
/// ColdCeph concludes from output shaped like Ceph's — not about the argv it used to get it.
/// </summary>
[TestFixture]
public sealed class CephCliQueryProviderTests
{
    // ---- argv ----------------------------------------------------------------

    /// <summary>
    /// Three invocations, not four: PG states and capacity both come out of <c>status</c>'s
    /// <c>pgmap</c>, so neither needs a call of its own.
    /// </summary>
    [Test]
    public void Confirmation_issues_json_health_quorum_and_status_once_each()
    {
        var provider = CephFixture.Provider(CephFixture.Healthy, out var runner);

        _ = provider.GetObservation();

        Assert.That(runner.CountOf("health detail"), Is.EqualTo(1));
        Assert.That(runner.CountOf("quorum_status"), Is.EqualTo(1));
        Assert.That(runner.CountOf("status"), Is.EqualTo(2), "quorum_status also matches 'status'");
        Assert.That(runner.Commands, Has.Count.EqualTo(3));
        Assert.That(runner.Commands, Has.All.Contains("--format json"));
        Assert.That(runner.Commands, Has.None.Contains("ok-to-stop"));
        Assert.That(runner.Commands, Has.None.Contains("pg stat"));
    }

    /// <summary>
    /// The old provider derived six predicates through a health-detail call each, relying on a
    /// two-second cache to collapse them. On a cluster where <c>health detail</c> takes longer
    /// than the TTL the cache expired mid-confirmation and each predicate spawned its own
    /// process. One confirmation is now one call because of how it is written.
    /// </summary>
    [Test]
    public void Confirmation_does_not_reissue_health_detail_per_signal()
    {
        var provider = CephFixture.Provider(CephFixture.Recovering, out var runner);

        var observation = provider.GetObservation();
        var signals = CephSignals.From(observation);

        Assert.That(signals.HasRecoveryOrBackfill, Is.True);
        Assert.That(signals.PgsClean, Is.False);
        Assert.That(runner.CountOf("health detail"), Is.EqualTo(1));
    }

    [Test]
    public void Two_confirmations_both_reach_ceph_rather_than_sharing_a_cached_reading()
    {
        var provider = CephFixture.Provider(CephFixture.Healthy, out var runner);

        _ = provider.GetObservation();
        _ = provider.GetObservation();

        Assert.That(runner.CountOf("health detail"), Is.EqualTo(2));
    }

    [Test]
    public void Membership_is_its_own_confirmation_and_reads_only_osd_dump()
    {
        var provider = CephFixture.Provider(CephFixture.Healthy, out var runner);

        _ = provider.ListOsdMembership();

        Assert.That(runner.CountOf("osd dump"), Is.EqualTo(1));
        Assert.That(runner.CountOf("health detail"), Is.EqualTo(0));
    }

    [Test]
    public void Configured_conf_and_keyring_are_passed_through()
    {
        var runner = CephFixture.Runner(CephFixture.Healthy);
        var provider = new CephCliQueryProvider(
            new ControlConfig { CephBinary = "ceph", CephConf = "/tmp/ceph.conf", CephKeyring = "/tmp/ceph.keyring" },
            runner);

        _ = provider.GetObservation();

        Assert.That(runner.Commands, Has.All.Contains("--conf /tmp/ceph.conf"));
        Assert.That(runner.Commands, Has.All.Contains("--keyring /tmp/ceph.keyring"));
    }

    [Test]
    public void Conf_flags_are_absent_when_unset()
    {
        var provider = CephFixture.Provider(CephFixture.Healthy, out var runner);

        _ = provider.GetObservation();

        Assert.That(runner.Commands, Has.None.Contains("--conf"));
        Assert.That(runner.Commands, Has.None.Contains("--keyring"));
    }

    [Test]
    public void Container_mode_docker_execs_ceph()
    {
        var runner = CephFixture.Runner(CephFixture.Healthy);
        var provider = new CephCliQueryProvider(new ControlConfig { CephContainer = "abc123" }, runner);

        _ = provider.ListOsdMembership();

        Assert.That(runner.Commands.Single(), Is.EqualTo("docker exec abc123 ceph --format json osd dump"));
    }

    [Test]
    public void A_relative_script_is_refused_before_anything_starts()
    {
        var runner = CephFixture.Runner(CephFixture.Healthy);
        var provider = new CephCliQueryProvider(new ControlConfig { CephBinary = "docker/ceph/ceph" }, runner);

        Assert.That(() => provider.GetObservation(), Throws.InvalidOperationException);
        Assert.That(runner.Commands, Is.Empty);
    }

    // ---- quorum -------------------------------------------------------------

    [Test]
    public void Quorum_is_available_when_the_monitor_names_one()
    {
        Assert.That(CephFixture.Provider(CephFixture.Healthy).GetObservation().QuorumAvailable, Is.True);
    }

    /// <summary>
    /// The negative case the old provider could not express: <c>quorum_status</c> JSON always
    /// contains the string "quorum" in its key names, so a substring test over the document
    /// read as available even with an empty quorum.
    /// </summary>
    [Test]
    public void Quorum_is_unavailable_when_the_named_quorum_is_empty()
    {
        var json = CephFixture.Read(CephFixture.NoQuorum, "quorum_status.json");

        Assert.That(json, Does.Contain("quorum"));
        Assert.That(CephFixture.Provider(CephFixture.NoQuorum).GetObservation().QuorumAvailable, Is.False);
    }

    // ---- PG state -----------------------------------------------------------

    [Test]
    public void Healthy_pgs_are_active_and_clean()
    {
        var signals = CephFixture.Signals(CephFixture.Healthy);

        Assert.That(signals.PgsActive, Is.True);
        Assert.That(signals.PgsClean, Is.True);
    }

    /// <summary>
    /// The old <c>GetPgsClean</c> asked whether the document contained "active+clean"
    /// anywhere, so a cluster with one clean PG and thirty-two degraded ones read as clean.
    /// </summary>
    [TestCase(CephFixture.Recovering)]
    [TestCase(CephFixture.WakingPeering)]
    [TestCase(CephFixture.Unfound)]
    public void Pgs_are_not_clean_when_any_group_is_not_clean(string scenario)
    {
        var json = CephFixture.Read(scenario, "status.json");

        Assert.That(json, Does.Contain("active+clean"));
        Assert.That(CephFixture.Signals(scenario).PgsClean, Is.False);
    }

    /// <summary>
    /// The old <c>GetPgsActive</c> never looked at PG state at all — it negated a substring
    /// test over health text — so peering PGs read as active and gated reads open.
    /// </summary>
    [Test]
    public void Peering_pgs_are_not_active()
    {
        Assert.That(CephFixture.Signals(CephFixture.WakingPeering).PgsActive, Is.False);
    }

    /// <summary>
    /// A cold appliance's PGs read <c>stale+active+clean</c>: Ceph keeps the state it last saw
    /// and adds <c>stale</c> to say the primary is now unreachable. So the <c>active</c> token is
    /// still there, and availability has to come from <c>stale</c> — which is why reads are closed
    /// by <see cref="CephSignals.HasStaleOrDown"/> rather than by the active token alone.
    /// </summary>
    [Test]
    public void Stale_pgs_close_reads_although_they_still_carry_the_active_token()
    {
        var signals = CephFixture.Signals(CephFixture.ColdOsdsDown);

        Assert.That(signals.HasStaleOrDown, Is.True);
        Assert.That(signals.PgsActive, Is.True);
        Assert.That(signals.ToPredicates(osdPlaneExpected: true).ReadReady, Is.False);
    }

    /// <summary>
    /// Damage does not clear the clean token either: an inconsistent PG is
    /// <c>active+clean+inconsistent</c>. Writes are held by the damage signal, not by cleanliness.
    /// </summary>
    [Test]
    public void Damaged_pgs_still_carry_the_clean_token()
    {
        var signals = CephFixture.Signals(CephFixture.Inconsistent);

        Assert.That(signals.PgsClean, Is.True);
        Assert.That(signals.HasInconsistent, Is.True);
        Assert.That(signals.ToPredicates(osdPlaneExpected: true).WriteReady, Is.False);
        Assert.That(signals.ToPredicates(osdPlaneExpected: true).SleepSafe, Is.False);
    }

    /// <summary>
    /// A confirmation that reports no PG states must not read as ready. This is the case that was
    /// silently true against the live cluster: PG states were read from a command whose JSON did not
    /// carry them, so every readiness predicate was false on a healthy HEALTH_OK cluster.
    /// </summary>
    [Test]
    public void A_confirmation_reporting_no_pgs_is_neither_active_nor_clean()
    {
        var runner = CephFixture.Runner(CephFixture.Healthy)
            .Answer("status", """{"pgmap":{"num_pgs":0,"bytes_total":3000,"bytes_used":1000,"bytes_avail":2000}}""");
        var provider = new CephCliQueryProvider(new ControlConfig { CephBinary = "ceph" }, runner);

        var signals = CephSignals.From(provider.GetObservation());

        Assert.That(signals.PgsActive, Is.False);
        Assert.That(signals.PgsClean, Is.False);
    }

    /// <summary>Capacity and PG states are independent readings of one document.</summary>
    [Test]
    public void A_status_without_capacity_still_yields_pg_states()
    {
        var runner = CephFixture.Runner(CephFixture.Healthy)
            .Answer("status", """{"pgmap":{"pgs_by_state":[{"state_name":"active+clean","count":4}]}}""");
        var provider = new CephCliQueryProvider(new ControlConfig { CephBinary = "ceph" }, runner);

        var observation = provider.GetObservation();

        Assert.That(observation.PgStates, Has.Count.EqualTo(1));
        Assert.That(observation.Capacity, Is.Null);
        Assert.That(observation.CapacityUnavailableReason, Is.Not.Null);
        Assert.That(CephSignals.From(observation).PgsClean, Is.True);
    }

    /// <summary>A status the provider cannot read at all fails closed on PG state.</summary>
    [Test]
    public void A_failing_status_call_leaves_no_pg_states()
    {
        var runner = CephFixture.Runner(CephFixture.Healthy).Fail("status", "connection timed out");
        var provider = new CephCliQueryProvider(new ControlConfig { CephBinary = "ceph" }, runner);

        var observation = provider.GetObservation();

        Assert.That(observation.PgStates, Is.Empty);
        Assert.That(observation.CapacityUnavailableReason, Does.Contain("timed out"));
        Assert.That(CephSignals.From(observation).PgsActive, Is.False);
    }

    // ---- durability signals -------------------------------------------------

    [Test]
    public void Unfound_objects_are_a_durability_failure()
    {
        var signals = CephFixture.Signals(CephFixture.Unfound);

        Assert.That(signals.HasUnfound, Is.True);
        Assert.That(signals.DurabilityFailure, Is.True);
    }

    /// <summary>
    /// Ceph reports inconsistency as <c>PG_DAMAGED</c> and <c>OSD_SCRUB_ERRORS</c>. Neither
    /// name contains the word "inconsistent", so name-substring matching missed both.
    /// </summary>
    [Test]
    public void Damaged_pgs_are_a_durability_failure_although_no_check_name_says_inconsistent()
    {
        var observation = CephFixture.Observation(CephFixture.Inconsistent);

        Assert.That(observation.HealthChecks.Select(check => check.Name),
            Is.EquivalentTo(new[] { "PG_DAMAGED", "OSD_SCRUB_ERRORS" }));
        Assert.That(observation.HealthChecks.Any(check => check.Name.Contains("inconsistent", StringComparison.OrdinalIgnoreCase)), Is.False);
        Assert.That(CephSignals.From(observation).HasInconsistent, Is.True);
    }

    [Test]
    public void Incomplete_pgs_are_a_durability_failure()
    {
        var signals = CephFixture.Signals(CephFixture.Incomplete);

        Assert.That(signals.HasIncomplete, Is.True);
        Assert.That(signals.DurabilityFailure, Is.True);
    }

    /// <summary>
    /// <c>PG_AVAILABILITY</c> covers both a benign wake and an incomplete PG, which is why the
    /// verdict comes from PG state tokens rather than the check name alone.
    /// </summary>
    [Test]
    public void Peering_pgs_under_the_same_check_name_are_not_a_durability_failure()
    {
        var waking = CephFixture.Observation(CephFixture.WakingPeering);
        var incomplete = CephFixture.Observation(CephFixture.Incomplete);

        Assert.That(waking.HealthChecks.Select(check => check.Name), Does.Contain("PG_AVAILABILITY"));
        Assert.That(incomplete.HealthChecks.Select(check => check.Name), Does.Contain("PG_AVAILABILITY"));
        Assert.That(CephSignals.From(waking).DurabilityFailure, Is.False);
        Assert.That(CephSignals.From(incomplete).DurabilityFailure, Is.True);
    }

    [TestCase(CephFixture.Healthy)]
    [TestCase(CephFixture.DemoWarnings)]
    [TestCase(CephFixture.ColdOsdsDown)]
    [TestCase(CephFixture.WakingPeering)]
    [TestCase(CephFixture.ScopedNoout)]
    [TestCase(CephFixture.NearFull)]
    [TestCase(CephFixture.Recovering)]
    public void Ordinary_conditions_are_not_durability_failures(string scenario)
    {
        Assert.That(CephFixture.Signals(scenario).DurabilityFailure, Is.False);
    }

    [Test]
    public void Nearfull_osds_are_reported_and_are_not_a_durability_failure()
    {
        var signals = CephFixture.Signals(CephFixture.NearFull);

        Assert.That(signals.HasFullOsds, Is.True);
        Assert.That(signals.DurabilityFailure, Is.False);
    }

    [Test]
    public void Recovery_and_backfill_are_reported_separately_from_loss()
    {
        var signals = CephFixture.Signals(CephFixture.Recovering);

        Assert.That(signals.HasRecoveryOrBackfill, Is.True);
        Assert.That(signals.HasUnfound, Is.False);
    }

    [Test]
    public void A_cold_cluster_is_not_recovering()
    {
        Assert.That(CephFixture.Signals(CephFixture.ColdOsdsDown).HasRecoveryOrBackfill, Is.False);
    }

    // ---- health checks and status ------------------------------------------

    [Test]
    public void Health_checks_are_parsed_as_names_severities_and_messages()
    {
        var observation = CephFixture.Observation(CephFixture.ColdOsdsDown);

        Assert.That(observation.Health.Status, Is.EqualTo("HEALTH_WARN"));
        Assert.That(observation.HealthChecks.Select(check => check.Name),
            Is.EquivalentTo(new[] { "OSD_DOWN", "OSD_HOST_DOWN", "PG_AVAILABILITY" }));
        var down = observation.HealthChecks.Single(check => check.Name == "OSD_DOWN");
        Assert.That(down.Severity, Is.EqualTo("HEALTH_WARN"));
        Assert.That(down.Message, Is.EqualTo("3 osds down"));
        Assert.That(down.Display, Is.EqualTo("OSD_DOWN: 3 osds down"));
    }

    [Test]
    public void A_healthy_confirmation_has_a_status_and_no_checks()
    {
        var observation = CephFixture.Observation(CephFixture.Healthy);

        Assert.That(observation.Health.Status, Is.EqualTo("HEALTH_OK"));
        Assert.That(observation.HealthChecks, Is.Empty);
        Assert.That(observation.Health.Summary, Is.EqualTo("HEALTH_OK"));
    }

    [Test]
    public void An_error_status_is_not_rewritten_as_ok()
    {
        Assert.That(CephFixture.Observation(CephFixture.Unfound).Health.Status, Is.EqualTo("HEALTH_ERR"));
    }

    [Test]
    public void Missing_status_reads_as_error_rather_than_ok()
    {
        var runner = CephFixture.Runner(CephFixture.Healthy).Answer("health detail", "{}");
        var provider = new CephCliQueryProvider(new ControlConfig { CephBinary = "ceph" }, runner);

        Assert.That(provider.GetObservation().Health.Status, Is.EqualTo("HEALTH_ERR"));
    }

    // ---- osd dump -----------------------------------------------------------

    [Test]
    public void Osd_dump_parses_numeric_up_and_in()
    {
        var runner = CephFixture.Runner(CephFixture.Healthy)
            .Answer("osd dump", """{"osds":[{"osd":0,"up":1,"in":1},{"osd":1,"up":0,"in":1}]}""");
        var provider = new CephCliQueryProvider(new ControlConfig { CephBinary = "ceph" }, runner);

        var membership = provider.ListOsdMembership();

        Assert.That(membership[0].Up, Is.True);
        Assert.That(membership[0].In, Is.True);
        Assert.That(membership[1].Up, Is.False);
        Assert.That(membership[1].In, Is.True);
    }

    [Test]
    public void Osd_dump_parses_boolean_up_and_in()
    {
        var runner = CephFixture.Runner(CephFixture.Healthy)
            .Answer("osd dump", """{"osds":[{"osd":4,"up":true,"in":false}]}""");
        var provider = new CephCliQueryProvider(new ControlConfig { CephBinary = "ceph" }, runner);

        var membership = provider.ListOsdMembership();

        Assert.That(membership[4].Up, Is.True);
        Assert.That(membership[4].In, Is.False);
    }

    [Test]
    public void Osd_dump_treats_a_missing_flag_as_not_up()
    {
        var runner = CephFixture.Runner(CephFixture.Healthy)
            .Answer("osd dump", """{"osds":[{"osd":9}]}""");
        var provider = new CephCliQueryProvider(new ControlConfig { CephBinary = "ceph" }, runner);

        Assert.That(provider.ListOsdMembership()[9].Up, Is.False);
    }

    [Test]
    public void Osd_dump_without_osds_is_empty()
    {
        var runner = CephFixture.Runner(CephFixture.Healthy).Answer("osd dump", """{"fsid":"abc"}""");
        var provider = new CephCliQueryProvider(new ControlConfig { CephBinary = "ceph" }, runner);

        Assert.That(provider.ListOsdMembership(), Is.Empty);
    }

    [Test]
    public void Healthy_fixture_reports_every_osd_up_and_in()
    {
        var membership = CephFixture.Provider(CephFixture.Healthy).ListOsdMembership();

        Assert.That(membership, Has.Count.EqualTo(3));
        Assert.That(membership.Values.All(osd => osd.Up && osd.In), Is.True);
    }

    // ---- capacity -----------------------------------------------------------

    [Test]
    public void Status_parses_cluster_capacity()
    {
        var capacity = CephFixture.Observation(CephFixture.Healthy).Capacity;

        Assert.That(capacity, Is.Not.Null);
        Assert.That(capacity!.TotalBytes, Is.EqualTo(3_000_000_000));
        Assert.That(capacity.UsedBytes, Is.EqualTo(1_000_000_000));
        Assert.That(capacity.AvailableBytes, Is.EqualTo(2_000_000_000));
    }

    [TestCase("{}")]
    [TestCase("""{"pgmap":{"bytes_total":-1,"bytes_used":0,"bytes_avail":0}}""")]
    [TestCase("""{"pgmap":{"bytes_used":0,"bytes_avail":0}}""")]
    public void Invalid_capacity_is_reported_unavailable_without_losing_the_health_reading(string status)
    {
        var runner = CephFixture.Runner(CephFixture.ColdOsdsDown).Answer("status", status);
        var provider = new CephCliQueryProvider(new ControlConfig { CephBinary = "ceph" }, runner);

        var observation = provider.GetObservation();

        Assert.That(observation.Capacity, Is.Null);
        Assert.That(observation.CapacityUnavailableReason, Is.Not.Null);
        Assert.That(observation.Health.Status, Is.EqualTo("HEALTH_WARN"));
        Assert.That(observation.HealthChecks, Is.Not.Empty);
    }

    // ---- CLI failure --------------------------------------------------------

    [Test]
    public void A_failing_ceph_invocation_propagates_so_the_caller_can_fail_closed()
    {
        var runner = CephFixture.Runner(CephFixture.Healthy)
            .Fail("health detail", "unable to get monitor info from DNS SRV with service name: ceph-mon");
        var provider = new CephCliQueryProvider(new ControlConfig { CephBinary = "ceph" }, runner);

        Assert.That(() => provider.GetObservation(),
            Throws.InvalidOperationException.With.Message.Contains("monitor info"));
    }

    [Test]
    public void A_timed_out_ceph_invocation_propagates()
    {
        var runner = CephFixture.Runner(CephFixture.Healthy).Hang("quorum_status");
        var provider = new CephCliQueryProvider(new ControlConfig { CephBinary = "ceph" }, runner);

        Assert.That(() => provider.GetObservation(), Throws.TypeOf<TimeoutException>());
    }

    [Test]
    public void Empty_output_does_not_crash_the_parser()
    {
        var runner = CephFixture.Runner(CephFixture.Healthy)
            .Answer("health detail", "")
            .Answer("quorum_status", "")
            .Answer("status", "");
        var provider = new CephCliQueryProvider(new ControlConfig { CephBinary = "ceph" }, runner);

        var observation = provider.GetObservation();

        Assert.That(observation.Health.Status, Is.EqualTo("HEALTH_ERR"));
        Assert.That(observation.QuorumAvailable, Is.False);
        Assert.That(observation.PgStates, Is.Empty);
    }
}

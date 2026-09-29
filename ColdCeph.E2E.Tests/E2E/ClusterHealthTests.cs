using ColdCeph.E2E.Tests.Extensions;
using ColdCeph.E2E.Tests.Scenarios;
using ColdCeph.E2E.Tests.Support;
using Xcepto;

namespace ColdCeph.E2E.Tests.E2E;

[TestFixture]
public sealed class ClusterHealthTests
{
    [Test]
    public async Task Dashboard_keeps_cold_operational_state_distinct_from_ceph_health()
    {
        var scenario = new ColdCephScenario();
        await XceptoTest.Given(scenario, SharedTimeout.Timeout, builder =>
        {
            var op = builder.OperatorAdapterBuilder()
                .WithBaseUrl(scenario.ControlAddress)
                .Build();

            op.EnsureCold(scenario.OperatorPassword);
            op.SeeStoragePlane("Cold");
            op.SeeCephHealthExists();
            op.SeeOverviewCapacity();
        });
    }

    /// <summary>
    /// The journey the harness used to make impossible by muting the cluster's warnings: whatever
    /// a real small Ceph is reporting, ColdCeph classifies it as expected and does not read it as
    /// data loss.
    /// </summary>
    [Test]
    public async Task Live_ceph_health_is_classified_rather_than_muted()
    {
        var scenario = new ColdCephScenario();
        await XceptoTest.Given(scenario, SharedTimeout.Timeout, builder =>
        {
            var op = builder.OperatorAdapterBuilder()
                .WithBaseUrl(scenario.ControlAddress)
                .Build();

            op.EnsureCold(scenario.OperatorPassword);
            op.SeeLiveHealthIsClassifiedExpected();
            op.SeeLiveHealthIsNotADurabilityFailure();
        });
    }

    [Test]
    public async Task Wake_reaches_ready_against_ceph()
    {
        var scenario = new ColdCephScenario();
        await XceptoTest.Given(scenario, SharedTimeout.Timeout, builder =>
        {
            var op = builder.OperatorAdapterBuilder()
                .WithBaseUrl(scenario.ControlAddress)
                .Build();

            op.EnsureCold(scenario.OperatorPassword);
            op.Wake();
            op.SeeStoragePlane("Ready");
            op.SeeLiveHealthIsClassifiedExpected();
        });
    }

    [Test]
    public async Task Sleep_reaches_cold_against_ceph()
    {
        var scenario = new ColdCephScenario();
        await XceptoTest.Given(scenario, SharedTimeout.Timeout, builder =>
        {
            var op = builder.OperatorAdapterBuilder()
                .WithBaseUrl(scenario.ControlAddress)
                .Build();

            op.EnsureReady(scenario.OperatorPassword);
            op.Sleep();
            op.SeeStoragePlane("Cold");
        });
    }

    /// <summary>
    /// A slept appliance holds a scoped noout, and Ceph raises a flags check for it. The
    /// classification of that check is what keeps a sleeping appliance out of FAULTED, and until
    /// now it had only ever been asserted against a hand-written string.
    /// </summary>
    [Test]
    public async Task A_slept_appliance_classifies_its_own_noout_as_expected()
    {
        var scenario = new ColdCephScenario();
        await XceptoTest.Given(scenario, SharedTimeout.LongTimeout, builder =>
        {
            var op = builder.OperatorAdapterBuilder()
                .WithBaseUrl(scenario.ControlAddress)
                .Build();

            op.EnsureReady(scenario.OperatorPassword);
            op.Sleep();
            op.SeeStoragePlane("Cold");
            op.SeeOwnedNooutIsClassifiedExpected();
        });
    }
}

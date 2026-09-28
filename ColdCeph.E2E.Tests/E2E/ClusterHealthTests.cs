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
}

using ColdCeph.E2E.Tests.Extensions;
using ColdCeph.E2E.Tests.Scenarios;
using ColdCeph.E2E.Tests.Support;
using Xcepto;

namespace ColdCeph.E2E.Tests.E2E;

[TestFixture]
public sealed class CephClusterTests
{
    [Test]
    public async Task Control_ceph_cli_runs_against_the_cluster()
    {
        var scenario = new ColdCephScenario();
        await XceptoTest.Given(scenario, SharedTimeout.Timeout, builder =>
        {
            var ceph = builder.CephAdapterBuilder()
                .WithContainer(scenario.CephContainer)
                .Build();

            ceph.SeeQuorum();
            ceph.SeeHealthNotSilent();
            ceph.SeePgStat();
            ceph.SeeOsdMembership();
            ceph.SeeScopedNooutRoundTrip();
        });
    }
}

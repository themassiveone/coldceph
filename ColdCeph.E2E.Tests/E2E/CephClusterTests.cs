using ColdCeph.E2E.Tests.Extensions;
using ColdCeph.E2E.Tests.Scenarios;
using ColdCeph.E2E.Tests.Support;
using Xcepto;

namespace ColdCeph.E2E.Tests.E2E;

[TestFixture]
public sealed class CephClusterTests
{
    [Test]
    public async Task Cluster_exposes_mon_health_and_osds()
    {
        var scenario = new ColdCephScenario();
        await XceptoTest.Given(scenario, SharedTimeout.Timeout, builder =>
        {
            var ceph = builder.CephAdapterBuilder()
                .WithBinary(scenario.CephBinary)
                .WithWorkingDirectory(scenario.RepositoryRoot)
                .Build();

            ceph.SeeQuorum();
            ceph.SeeHealthNotSilent();
            ceph.SeeOsdMap();
        });
    }
}

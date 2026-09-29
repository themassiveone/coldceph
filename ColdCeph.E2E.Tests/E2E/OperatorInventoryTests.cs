using ColdCeph.E2E.Tests.Extensions;
using ColdCeph.E2E.Tests.Scenarios;
using ColdCeph.E2E.Tests.Support;
using Xcepto;

namespace ColdCeph.E2E.Tests.E2E;

[TestFixture]
public sealed class OperatorInventoryTests
{
    [Test]
    public async Task Allowed_node_push_populates_osds_and_devices()
    {
        var scenario = new ColdCephScenario();
        await XceptoTest.Given(scenario, SharedTimeout.Timeout, builder =>
        {
            var op = builder.OperatorAdapterBuilder()
                .WithBaseUrl(scenario.ControlAddress)
                .Build();

            op.SeeOsds(scenario.OperatorPassword);
            op.SeeDevices(scenario.OperatorPassword);
            op.SeeOsdOverlayReportsCephMembership();
        });
    }
}

using ColdCeph.E2E.Tests.Extensions;
using ColdCeph.E2E.Tests.Scenarios;
using ColdCeph.E2E.Tests.Support;
using Xcepto;

namespace ColdCeph.E2E.Tests.E2E;

[TestFixture]
public sealed class NodeAuthTests
{
    [Test]
    public async Task Node_status_requires_token_then_returns_host_identity()
    {
        var scenario = new ColdCephScenario();
        await XceptoTest.Given(scenario, SharedTimeout.Timeout, builder =>
        {
            var node = builder.NodeAdapterBuilder()
                .WithBaseUrl(scenario.NodeAddress)
                .WithToken(scenario.NodeToken)
                .Build();

            node.SeeUnauthorizedWithoutToken();
            node.SeeHost(scenario.HostId);
            node.SeeOsds();
        });
    }
}

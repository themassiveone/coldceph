using ColdCeph.E2E.Tests.Extensions;
using ColdCeph.E2E.Tests.Scenarios;
using ColdCeph.E2E.Tests.Support;
using Xcepto;

namespace ColdCeph.E2E.Tests.E2E;

[TestFixture]
public sealed class AgentAuthTests
{
    [Test]
    public async Task Agent_status_requires_token_then_returns_host_identity()
    {
        var scenario = new ColdCephScenario();
        await XceptoTest.Given(scenario, SharedTimeout.Timeout, builder =>
        {
            var agent = builder.AgentAdapterBuilder()
                .WithBaseUrl(scenario.AgentAddress)
                .WithToken(scenario.AgentToken)
                .Build();

            agent.SeeUnauthorizedWithoutToken();
            agent.SeeHost(scenario.HostId);
            agent.SeeOsds();
        });
    }
}

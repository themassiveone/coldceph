using System.Net;
using ColdCeph.E2E.Tests.Support;
using Xcepto;
using Xcepto.Rest.Extensions;
using Xcepto.SSR.Extensions;

namespace ColdCeph.E2E.Tests.E2E;

[TestFixture]
public sealed class OperatorJourneyTests
{
    [Test]
    public async Task Anonymous_home_is_unauthorized()
    {
        var scenario = new ColdCephScenario();
        await XceptoTest.Given(scenario, builder =>
        {
            var ssr = builder.SsrAdapterBuilder()
                .WithBaseUrl(scenario.ControlAddress)
                .Build();

            ssr.Get("/")
                .WithRetry(false)
                .AssertThatResponseStatus(Is.EqualTo(HttpStatusCode.Unauthorized));
        });
    }

    [Test]
    public async Task Health_reports_cold()
    {
        var scenario = new ColdCephScenario();
        await XceptoTest.Given(scenario, builder =>
        {
            var ssr = builder.SsrAdapterBuilder()
                .WithBaseUrl(scenario.ControlAddress)
                .Build();

            ssr.Get("/health")
                .AssertSuccess()
                .AssertThatResponseContentString(Does.Contain("Cold"));
        });
    }

    [Test]
    public async Task Agent_status_without_token_is_unauthorized()
    {
        var scenario = new ColdCephScenario();
        await XceptoTest.Given(scenario, builder =>
        {
            var rest = builder.RestAdapterBuilder()
                .WithBaseUrl(scenario.AgentAddress)
                .Build();

            rest.Get("/v1/status")
                .WithRetry(false)
                .AssertThatResponseStatus(Is.EqualTo(HttpStatusCode.Unauthorized));
        });
    }
}

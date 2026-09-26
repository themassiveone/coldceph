using ColdCeph.E2E.Tests.Extensions;
using ColdCeph.E2E.Tests.Scenarios;
using ColdCeph.E2E.Tests.Support;
using Xcepto;

namespace ColdCeph.E2E.Tests.E2E;

[TestFixture]
public sealed class OperatorAuthTests
{
    [Test]
    public async Task Anonymous_operator_is_rejected()
    {
        var scenario = new ColdCephScenario();
        await XceptoTest.Given(scenario, SharedTimeout.Timeout, builder =>
        {
            var anonymous = builder.OperatorAdapterBuilder()
                .WithBaseUrl(scenario.ControlAddress)
                .Build();

            anonymous.SeeUnauthorizedHome();
            anonymous.OpenLogin();
            anonymous.LoginRejected("wrong-password");
        });
    }

    [Test]
    public async Task Operator_can_sign_in_and_see_the_dashboard()
    {
        var scenario = new ColdCephScenario();
        await XceptoTest.Given(scenario, SharedTimeout.Timeout, builder =>
        {
            var op = builder.OperatorAdapterBuilder()
                .WithBaseUrl(scenario.ControlAddress)
                .Build();

            op.OpenLogin();
            op.Login(scenario.OperatorPassword);
            op.SeeDashboard();
        });
    }
}

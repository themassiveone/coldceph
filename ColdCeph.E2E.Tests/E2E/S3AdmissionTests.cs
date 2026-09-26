using ColdCeph.E2E.Tests.Extensions;
using ColdCeph.E2E.Tests.Scenarios;
using ColdCeph.E2E.Tests.Support;
using Xcepto;

namespace ColdCeph.E2E.Tests.E2E;

[TestFixture]
public sealed class S3AdmissionTests
{
    [Test]
    public async Task Get_while_cold_returns_retry_after()
    {
        var scenario = new ColdCephScenario();
        await XceptoTest.Given(scenario, SharedTimeout.Timeout, builder =>
        {
            var op = builder.OperatorAdapterBuilder()
                .WithBaseUrl(scenario.ControlAddress)
                .Build();
            var s3 = builder.S3AdapterBuilder()
                .WithBaseUrl(scenario.S3Address)
                .Build();

            op.EnsureCold(scenario.OperatorPassword);
            op.SeeStoragePlane("Cold");
            s3.SeeUnavailable();
        });
    }

    [Test]
    public async Task Get_after_wake_is_forwarded_to_rgw()
    {
        var scenario = new ColdCephScenario();
        await XceptoTest.Given(scenario, SharedTimeout.Timeout, builder =>
        {
            var op = builder.OperatorAdapterBuilder()
                .WithBaseUrl(scenario.ControlAddress)
                .Build();
            var s3 = builder.S3AdapterBuilder()
                .WithBaseUrl(scenario.S3Address)
                .Build();

            op.EnsureReady(scenario.OperatorPassword);
            op.SeeStoragePlane("Ready");
            s3.SeeForwarded();
        });
    }
}

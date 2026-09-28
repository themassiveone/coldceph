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
                .WithBucket(scenario.DemoBucket)
                .Build();

            op.EnsureCold(scenario.OperatorPassword);
            op.SeeStoragePlane("Cold");
            s3.SeeUnavailable();
        });
    }

    /// <summary>
    /// A missing object answering 404 proves the request reached RGW and was accepted, which
    /// "not 503" did not.
    /// </summary>
    [Test]
    public async Task Get_after_wake_reaches_rgw()
    {
        var scenario = new ColdCephScenario();
        await XceptoTest.Given(scenario, SharedTimeout.LongTimeout, builder =>
        {
            var op = builder.OperatorAdapterBuilder()
                .WithBaseUrl(scenario.ControlAddress)
                .Build();
            var s3 = builder.S3AdapterBuilder()
                .WithBaseUrl(scenario.S3Address)
                .WithBucket(scenario.DemoBucket)
                .Build();

            op.EnsureReady(scenario.OperatorPassword);
            op.SeeStoragePlane("Ready");
            op.SeeConfirmationAdmitsReads();
            s3.SeeMissingObjectIsNotFound();
        });
    }

    /// <summary>
    /// The journey nothing in the repository used to make: an object written through the cold
    /// endpoint and read back. A dropped signed header shows up here as RGW's 403.
    /// </summary>
    [Test]
    public async Task An_object_round_trips_through_the_cold_endpoint()
    {
        var scenario = new ColdCephScenario();
        await XceptoTest.Given(scenario, SharedTimeout.LongTimeout, builder =>
        {
            var op = builder.OperatorAdapterBuilder()
                .WithBaseUrl(scenario.ControlAddress)
                .Build();
            var s3 = builder.S3AdapterBuilder()
                .WithBaseUrl(scenario.S3Address)
                .WithBucket(scenario.DemoBucket)
                .Build();

            op.EnsureReady(scenario.OperatorPassword);
            op.SeeConfirmationAdmitsWrites();
            s3.SeeObjectRoundTrip();
            s3.SeeBucketListing();
        });
    }
}

using System.Net;
using System.Net.Http.Json;
using ColdCeph.Control.Tests.Support;
using ColdCeph.Core.Features.Integrity.DTOs;

namespace ColdCeph.Control.Tests.Features.Integrity.HTTP;

/// <summary>
/// Integrity's <c>/v1</c> surface. It had no coverage at all, and it is one of the four places
/// AGENTS.md allows to reach the monitor — so "once per request" has to hold here too.
/// </summary>
[TestFixture]
public sealed class IntegrityApiHttpTests
{
    [Test]
    public async Task Anonymous_v1_is_401_without_a_login_redirect_and_without_touching_ceph()
    {
        using var factory = new ControlAppFactory();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        foreach (var path in new[] { "/v1/cluster/health", "/v1/cluster/integrity" })
        {
            var response = await client.GetAsync(path);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), path);
            Assert.That(response.Headers.Location, Is.Null, path);
        }

        Assert.That(factory.Ceph.ObservationCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task Health_confirms_ceph_exactly_once()
    {
        using var factory = new ControlAppFactory();
        using var client = await OperatorClient.SignedIn(factory);

        var response = await client.GetAsync("/v1/cluster/health");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(factory.Ceph.ObservationCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task Health_returns_the_raw_ceph_status()
    {
        using var factory = new ControlAppFactory();
        factory.Ceph.Seeing(CephFixture.ColdOsdsDown);
        using var client = await OperatorClient.SignedIn(factory);

        var raw = await client.GetFromJsonAsync<CephHealthRaw>("/v1/cluster/health");

        Assert.That(raw, Is.Not.Null);
        Assert.That(raw!.Status, Is.EqualTo("HEALTH_WARN"));
        Assert.That(raw.Checks, Is.Not.Empty);
    }

    /// <summary>
    /// A cold appliance's HEALTH_ERR is reported as-is and classified as expected. The API must
    /// not smooth it into HEALTH_OK, and it must not present it as a failure either.
    /// </summary>
    [Test]
    public async Task An_error_status_is_neither_hidden_nor_presented_as_a_failure()
    {
        using var factory = new ControlAppFactory();
        factory.Ceph.Seeing(CephFixture.Unfound);
        using var client = await OperatorClient.SignedIn(factory);

        var snapshot = await client.GetFromJsonAsync<IntegritySnapshot>("/v1/cluster/integrity");

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot!.Raw.Status, Is.EqualTo("HEALTH_ERR"));
        Assert.That(snapshot.DurabilityFailure, Is.True);
        Assert.That(snapshot.Predicates.WriteReady, Is.False);
    }

    [Test]
    public async Task The_integrity_document_carries_the_classified_checks_and_capacity()
    {
        using var factory = new ControlAppFactory();
        factory.Ceph.Seeing(CephFixture.DemoWarnings);
        using var client = await OperatorClient.SignedIn(factory);

        var snapshot = await client.GetFromJsonAsync<IntegritySnapshot>("/v1/cluster/integrity");

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot!.Checks.Select(check => check.Name), Does.Contain("TOO_FEW_OSDS"));
        Assert.That(snapshot.Checks.All(check => check.Classification == HealthClassification.ExpectedCold), Is.True);
        Assert.That(snapshot.Capacity?.TotalBytes, Is.EqualTo(3_000_000_000));
        Assert.That(factory.Ceph.ObservationCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task An_unreachable_monitor_is_reported_as_unavailable_rather_than_failing_the_request()
    {
        using var factory = new ControlAppFactory();
        factory.Ceph.ThrowOnObservation = true;
        using var client = await OperatorClient.SignedIn(factory);

        var response = await client.GetAsync("/v1/cluster/integrity");
        var snapshot = await response.Content.ReadFromJsonAsync<IntegritySnapshot>();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(snapshot!.Raw.Status, Is.EqualTo("UNAVAILABLE"));
        Assert.That(snapshot.Predicates.ReadReady, Is.False);
        Assert.That(snapshot.DurabilityFailure, Is.False);
    }

    [Test]
    public async Task Two_requests_are_two_confirmations()
    {
        using var factory = new ControlAppFactory();
        using var client = await OperatorClient.SignedIn(factory);

        _ = await client.GetAsync("/v1/cluster/integrity");
        _ = await client.GetAsync("/v1/cluster/integrity");

        Assert.That(factory.Ceph.ObservationCalls, Is.EqualTo(2));
    }
}

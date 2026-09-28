using System.Net;

namespace ColdCeph.Control.Tests.Features.Integrity.HTTP;

[TestFixture]
public sealed class IntegrityPagesHttpTests
{
    [Test]
    public async Task Integrity_html_redirects_to_overview()
    {
        using var factory = new Support.ControlAppFactory();
        using var client = await Support.OperatorClient.SignedIn(factory);

        var response = await client.GetAsync("/integrity");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(response.Headers.Location?.ToString(), Is.EqualTo("/"));
        Assert.That(factory.Ceph.HealthDetailCalls, Is.EqualTo(0));
        Assert.That(factory.Ceph.CapacityCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task Check_protection_confirms_ceph_once_and_returns_to_overview()
    {
        using var factory = new Support.ControlAppFactory();
        using var client = await Support.OperatorClient.SignedIn(factory);

        var response = await Support.OperatorClient.Post(client, "/integrity/check");
        var html = await client.GetStringAsync("/");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(response.Headers.Location?.ToString(), Is.EqualTo("/"));
        Assert.That(html, Does.Contain("3 GB"));
        Assert.That(html, Does.Contain("1 GB"));
        Assert.That(html, Does.Contain("2 GB"));
        Assert.That(html, Does.Contain("Protected when last checked"));
        Assert.That(html, Does.Not.Contain("Advanced"));
        Assert.That(factory.Ceph.HealthDetailCalls, Is.EqualTo(1));
        Assert.That(factory.Ceph.CapacityCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task Check_protection_when_ceph_is_down_does_not_claim_health_ok()
    {
        using var factory = new Support.ControlAppFactory();
        factory.Ceph.ThrowOnHealth = true;
        using var client = await Support.OperatorClient.SignedIn(factory);

        _ = await Support.OperatorClient.Post(client, "/integrity/check");
        var html = await client.GetStringAsync("/");

        Assert.That(html, Does.Contain("Not checked"));
        Assert.That(html, Does.Not.Contain("HEALTH_OK"));
        Assert.That(html, Does.Not.Contain("Protected when last checked"));
    }

    [Test]
    public async Task Check_protection_rejects_an_external_return_url()
    {
        using var factory = new Support.ControlAppFactory();
        using var client = await Support.OperatorClient.SignedIn(factory);

        var response = await Support.OperatorClient.Post(
            client,
            "/integrity/check",
            new Dictionary<string, string> { ["returnUrl"] = "https://example.com/" });

        Assert.That(response.Headers.Location?.ToString(), Is.EqualTo("/"));
    }
}

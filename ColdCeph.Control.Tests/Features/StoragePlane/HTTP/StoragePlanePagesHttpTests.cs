using System.Net;

namespace ColdCeph.Control.Tests.Features.StoragePlane.HTTP;

[TestFixture]
public sealed class StoragePlanePagesHttpTests
{
    [Test]
    public async Task Home_does_not_auto_refresh()
    {
        using var factory = new Support.ControlAppFactory();
        using var client = await Support.OperatorClient.SignedIn(factory);

        var html = await client.GetStringAsync("/");

        Assert.That(html, Does.Not.Contain("http-equiv=\"refresh\""));
        Assert.That(html, Does.Contain("Disks are parked"));
        Assert.That(html, Does.Contain("Wake disks"));
        Assert.That(html, Does.Not.Contain("Sleep disks"));
        Assert.That(factory.Ceph.HealthDetailCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task Home_does_not_offer_sleep_while_cold()
    {
        using var factory = new Support.ControlAppFactory();
        using var client = await Support.OperatorClient.SignedIn(factory);

        var html = await client.GetStringAsync("/");

        Assert.That(html, Does.Not.Contain("action=\"/sleep\""));
        Assert.That(html, Does.Contain("action=\"/wake\""));
    }

    [Test]
    public async Task Anonymous_home_still_sends_browsers_to_login()
    {
        using var factory = new Support.ControlAppFactory();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(response.Headers.Location?.ToString(), Is.EqualTo("/auth/login"));
    }
}

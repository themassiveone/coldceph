using System.Net;

namespace ColdCeph.Control.Tests.Features.StoragePlane.HTTP;

[TestFixture]
public sealed class StoragePlaneHttpTests
{
    [Test]
    public async Task Health_is_anonymous()
    {
        using var factory = new Support.ControlAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.That(response.IsSuccessStatusCode, Is.True);
        Assert.That(body, Does.Contain("Cold"));
    }

    [Test]
    public async Task Cluster_state_api_requires_auth()
    {
        using var factory = new Support.ControlAppFactory();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/v1/cluster/state");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}

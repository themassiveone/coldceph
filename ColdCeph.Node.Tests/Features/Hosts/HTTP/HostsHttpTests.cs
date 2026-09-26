using System.Net;

namespace ColdCeph.Node.Tests.Features.Hosts.HTTP;

[TestFixture]
public sealed class HostsHttpTests
{
    [Test]
    public async Task Status_requires_the_node_token()
    {
        using var factory = new Support.NodeAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/status");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Status_returns_host_identity_with_token()
    {
        using var factory = new Support.NodeAppFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-ColdCeph-Token", "secret");

        var response = await client.GetAsync("/v1/status");
        var body = await response.Content.ReadAsStringAsync();

        Assert.That(response.IsSuccessStatusCode, Is.True);
        Assert.That(body, Does.Contain("h1"));
    }
}

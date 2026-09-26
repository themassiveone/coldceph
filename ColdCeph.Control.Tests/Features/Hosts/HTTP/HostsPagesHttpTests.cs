using System.Net;
using ColdCeph.Control.Features.Hosts.Controllers;
using Microsoft.Extensions.DependencyInjection;

namespace ColdCeph.Control.Tests.Features.Hosts.HTTP;

[TestFixture]
public sealed class HostsPagesHttpTests
{
    [Test]
    public async Task Hosts_page_sends_anonymous_browsers_to_login()
    {
        using var factory = new Support.ControlAppFactory();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/hosts");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(response.Headers.Location?.ToString(), Is.EqualTo("/auth/login"));
    }

    [Test]
    public async Task Approve_without_login_does_not_enroll_a_host()
    {
        using var factory = new Support.ControlAppFactory();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>());

        var response = await client.PostAsync("/hosts/dev/approve", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(response.Headers.Location?.ToString(), Is.EqualTo("/auth/login"));
        Assert.That(factory.Services.GetRequiredService<HostsController>().GetHost("dev"), Is.Null);
    }
}

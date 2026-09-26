using System.Net;
using ColdCeph.Control.Features.Hosts.Controllers;
using ColdCeph.Core.Features.Hosts.DTOs;
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

    [Test]
    public async Task Empty_hosts_page_explains_how_a_node_joins()
    {
        using var factory = new Support.ControlAppFactory();
        using var client = await Support.OperatorClient.SignedIn(factory);

        var html = await client.GetStringAsync("/hosts");

        Assert.That(html, Does.Contain("No join requests"));
        Assert.That(html, Does.Contain("No machines enrolled"));
        Assert.That(html, Does.Not.Contain("http-equiv=\"refresh\""));
    }

    [Test]
    public async Task Pending_join_shows_allow_and_deny_on_that_row()
    {
        using var factory = new Support.ControlAppFactory();
        factory.Services.GetRequiredService<HostsController>().RequestJoin(
            new NodeStatusDto { HostId = "h1", Hostname = "rack-a", ObservedAt = DateTimeOffset.UtcNow },
            "http://127.0.0.1:7080");
        using var client = await Support.OperatorClient.SignedIn(factory);

        var html = await client.GetStringAsync("/hosts");

        Assert.That(html, Does.Contain("rack-a"));
        Assert.That(html, Does.Contain("action=\"/hosts/h1/approve\""));
        Assert.That(html, Does.Contain("action=\"/hosts/h1/deny\""));
        Assert.That(html, Does.Not.Contain("No join requests"));
    }
}

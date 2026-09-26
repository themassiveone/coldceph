using System.Net;
using System.Net.Http.Json;
using ColdCeph.Control.Features.Hosts.Controllers;
using ColdCeph.Core.Features.Hosts.DTOs;
using Microsoft.Extensions.DependencyInjection;

namespace ColdCeph.Control.Tests.Features.Hosts.HTTP;

[TestFixture]
public sealed class HostsJoinHttpTests
{
    [Test]
    public async Task Join_without_a_token_waits_for_approval()
    {
        using var factory = new Support.ControlAppFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-ColdCeph-Node-Endpoint", "http://127.0.0.1:7080");

        var response = await client.PostAsJsonAsync("/v1/hosts/join", new NodeStatusDto
        {
            HostId = "dev",
            Hostname = "dev",
            ObservedAt = DateTimeOffset.UtcNow
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        var hosts = factory.Services.GetRequiredService<HostsController>();
        Assert.That(hosts.GetHost("dev"), Is.Null);
        Assert.That(hosts.ListPendingJoins(), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Join_ignores_a_node_token_and_still_waits_for_approval()
    {
        using var factory = new Support.ControlAppFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-ColdCeph-Token", "changeme");
        client.DefaultRequestHeaders.Add("X-ColdCeph-Node-Endpoint", "http://127.0.0.1:7080");

        var response = await client.PostAsJsonAsync("/v1/hosts/join", new NodeStatusDto
        {
            HostId = "dev",
            Hostname = "dev",
            ObservedAt = DateTimeOffset.UtcNow
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        Assert.That(factory.Services.GetRequiredService<HostsController>().GetHost("dev"), Is.Null);
    }

    [Test]
    public async Task Join_does_not_enroll_when_the_operator_has_not_approved()
    {
        using var factory = new Support.ControlAppFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-ColdCeph-Node-Endpoint", "http://127.0.0.1:7080");
        await client.PostAsJsonAsync("/v1/hosts/join", new NodeStatusDto
        {
            HostId = "dev",
            Hostname = "dev",
            ObservedAt = DateTimeOffset.UtcNow
        });

        var listed = await client.GetAsync("/v1/hosts");

        Assert.That(listed.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(factory.Services.GetRequiredService<HostsController>().ListHosts(), Is.Empty);
    }

    [Test]
    public async Task Join_without_an_advertise_url_does_not_create_a_request()
    {
        using var factory = new Support.ControlAppFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/hosts/join", new NodeStatusDto
        {
            HostId = "dev",
            Hostname = "dev",
            ObservedAt = DateTimeOffset.UtcNow
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(factory.Services.GetRequiredService<HostsController>().ListPendingJoins(), Is.Empty);
    }
}

using System.Net;
using System.Net.Http.Json;
using ColdCeph.Control.Features.Hosts.Controllers;
using ColdCeph.Control.Features.Osds.Controllers;
using ColdCeph.Core.Features.Hosts.DTOs;
using ColdCeph.Core.Features.Osds.DTOs;
using Microsoft.Extensions.DependencyInjection;

namespace ColdCeph.Control.Tests.Features.Osds.HTTP;

[TestFixture]
public sealed class OsdsObservedHttpTests
{
    [Test]
    public async Task Observed_accepts_an_enrolled_host()
    {
        using var factory = new Support.ControlAppFactory();
        Enroll(factory, "dev");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-ColdCeph-Token", "changeme");

        var response = await client.PostAsJsonAsync("/v1/osds/observed", Observation("dev"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(factory.Services.GetRequiredService<OsdsController>().ListOsds().Select(osd => osd.OsdId), Does.Contain(0));
    }

    [Test]
    public async Task Observed_rejects_a_pending_host()
    {
        using var factory = new Support.ControlAppFactory();
        factory.Services.GetRequiredService<HostsController>().RequestJoin(
            new NodeStatusDto { HostId = "dev", Hostname = "dev", ObservedAt = DateTimeOffset.UtcNow },
            "http://127.0.0.1:7080");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-ColdCeph-Token", "changeme");

        var response = await client.PostAsJsonAsync("/v1/osds/observed", Observation("dev"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(factory.Services.GetRequiredService<OsdsController>().ListOsds(), Is.Empty);
    }

    [Test]
    public async Task Observed_rejects_a_missing_token()
    {
        using var factory = new Support.ControlAppFactory();
        Enroll(factory, "dev");
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/osds/observed", Observation("dev"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    private static void Enroll(Support.ControlAppFactory factory, string hostId)
    {
        var hosts = factory.Services.GetRequiredService<HostsController>();
        hosts.RequestJoin(
            new NodeStatusDto { HostId = hostId, Hostname = hostId, ObservedAt = DateTimeOffset.UtcNow },
            "http://127.0.0.1:7080");
        hosts.Approve(hostId);
    }

    private static HostOsdsObservationDto Observation(string hostId)
        => new()
        {
            HostId = hostId,
            Osds =
            [
                new OsdDto { OsdId = 0, HostId = hostId, DeviceId = "d0", Up = false, In = true, ProcessRunning = false }
            ]
        };
}

using System.Net;
using ColdCeph.Control.Features.Osds.Controllers;
using ColdCeph.Core.Features.Osds.DTOs;
using Microsoft.Extensions.DependencyInjection;

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
        Assert.That(factory.Ceph.MembershipCalls, Is.EqualTo(0));
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
    public async Task Home_stays_cold_when_every_host_has_pushed_stopped_osds()
    {
        using var factory = new Support.ControlAppFactory();
        var osds = factory.Services.GetRequiredService<OsdsController>();
        osds.ApplyObserved(new HostOsdsObservationDto
        {
            HostId = "node-a",
            Osds =
            [
                new OsdDto
                {
                    OsdId = 1,
                    HostId = "node-a",
                    DeviceId = "node-a-osd-1",
                    Up = false,
                    In = true,
                    ProcessRunning = false
                }
            ]
        });
        osds.ApplyObserved(new HostOsdsObservationDto
        {
            HostId = "node-b",
            Osds =
            [
                new OsdDto
                {
                    OsdId = 2,
                    HostId = "node-b",
                    DeviceId = "node-b-osd-2",
                    Up = false,
                    In = true,
                    ProcessRunning = false
                }
            ]
        });
        using var client = await Support.OperatorClient.SignedIn(factory);

        var html = await client.GetStringAsync("/");

        Assert.That(html, Does.Contain("Disks are parked"));
        Assert.That(html, Does.Contain("action=\"/wake\""));
        Assert.That(html, Does.Not.Contain("action=\"/sleep\""));
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

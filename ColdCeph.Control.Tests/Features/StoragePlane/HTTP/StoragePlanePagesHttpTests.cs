using System.Net;
using ColdCeph.Control.Features.StoragePlane.Services;
using ColdCeph.Control.Features.Osds.Controllers;
using ColdCeph.Control.Features.Devices.Controllers;
using ColdCeph.Control.Features.Hosts.Controllers;
using ColdCeph.Core.Features.Devices.DTOs;
using ColdCeph.Core.Features.Hosts.DTOs;
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
        Assert.That(html, Does.Contain("Finish setting up storage"));
        Assert.That(html, Does.Contain("Storage setup"));
        Assert.That(html, Does.Not.Contain("action=\"/wake\""));
        Assert.That(html, Does.Not.Contain("action=\"/sleep\""));
        Assert.That(factory.Ceph.HealthDetailCalls, Is.EqualTo(0));
        Assert.That(factory.Ceph.CapacityCalls, Is.EqualTo(0));
        Assert.That(factory.Ceph.MembershipCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task Home_does_not_offer_wake_or_sleep_before_setup()
    {
        using var factory = new Support.ControlAppFactory();
        using var client = await Support.OperatorClient.SignedIn(factory);

        var html = await client.GetStringAsync("/");

        Assert.That(html, Does.Not.Contain("action=\"/sleep\""));
        Assert.That(html, Does.Not.Contain("action=\"/wake\""));
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

        Assert.That(html, Does.Contain("Finish setting up storage"));
        Assert.That(html, Does.Not.Contain("action=\"/wake\""));
        Assert.That(html, Does.Not.Contain("action=\"/sleep\""));
    }

    [Test]
    public async Task Home_shows_capacity_and_wake_after_inventory_and_confirmation()
    {
        using var factory = new Support.ControlAppFactory();
        ConfigureStorage(factory);
        using var client = await Support.OperatorClient.SignedIn(factory);
        _ = await client.GetStringAsync("/integrity");

        var html = await client.GetStringAsync("/");

        Assert.That(html, Does.Contain("2 GB available"));
        Assert.That(html, Does.Contain("1 GB used of 3 GB"));
        Assert.That(html, Does.Contain("Protected when last checked"));
        Assert.That(html, Does.Contain("action=\"/wake\""));
        Assert.That(factory.Ceph.HealthDetailCalls, Is.EqualTo(1));
        Assert.That(factory.Ceph.CapacityCalls, Is.EqualTo(1));
        Assert.That(factory.Ceph.MembershipCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task Home_offers_resume_from_faulted_after_a_clean_confirmation()
    {
        using var factory = new Support.ControlAppFactory();
        ConfigureStorage(factory);
        factory.Services.GetRequiredService<StoragePlaneService>()
            .EnterFaulted("unexpected-integrity");
        using var client = await Support.OperatorClient.SignedIn(factory);
        _ = await client.GetStringAsync("/integrity");

        var html = await client.GetStringAsync("/");

        Assert.That(html, Does.Contain("action=\"/wake\""));
        Assert.That(html, Does.Contain("Resume storage"));
        Assert.That(html, Does.Not.Contain("Review problem"));
        Assert.That(html, Does.Contain("Wake storage to resume"));
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

    private static void ConfigureStorage(Support.ControlAppFactory factory)
    {
        var hosts = factory.Services.GetRequiredService<HostsController>();
        hosts.RequestJoin(
            new NodeStatusDto { HostId = "node-a", Hostname = "node-a", ObservedAt = DateTimeOffset.UtcNow },
            "http://127.0.0.1:7081");
        hosts.Approve("node-a");
        factory.Services.GetRequiredService<OsdsController>().ApplyObserved(new HostOsdsObservationDto
        {
            HostId = "node-a",
            Osds = [new OsdDto { OsdId = 0, HostId = "node-a", DeviceId = "disk-a", Up = false, In = true, ProcessRunning = false }]
        });
        factory.Services.GetRequiredService<DevicesController>().ApplyObserved(new HostDevicesObservationDto
        {
            HostId = "node-a",
            Devices =
            [
                new DeviceDto
                {
                    DeviceId = "disk-a",
                    HostId = "node-a",
                    MappedOsdId = 0,
                    Wwn = "wwn-a",
                    Serial = "serial-a",
                    Path = "/dev/sda",
                    PowerState = DevicePowerState.Standby
                }
            ]
        });
    }
}

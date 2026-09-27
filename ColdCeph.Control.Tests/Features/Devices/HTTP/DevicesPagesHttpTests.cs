using ColdCeph.Control.Features.Devices.Controllers;
using ColdCeph.Core.Features.Devices.DTOs;
using Microsoft.Extensions.DependencyInjection;

namespace ColdCeph.Control.Tests.Features.Devices.HTTP;

[TestFixture]
public sealed class DevicesPagesHttpTests
{
    [Test]
    public async Task Devices_page_explains_when_no_node_has_pushed()
    {
        using var factory = new Support.ControlAppFactory();
        using var client = await Support.OperatorClient.SignedIn(factory);

        var html = await client.GetStringAsync("/devices");

        Assert.That(html, Does.Contain("No drives reported"));
        Assert.That(html, Does.Not.Contain("cc-table"));
    }

    [Test]
    public async Task Devices_page_lists_pushed_inventory()
    {
        using var factory = new Support.ControlAppFactory();
        factory.Services.GetRequiredService<DevicesController>().ApplyObserved(new HostDevicesObservationDto
        {
            HostId = "dev",
            Devices =
            [
                new DeviceDto
                {
                    DeviceId = "d0",
                    HostId = "dev",
                    MappedOsdId = 0,
                    Wwn = "wwn",
                    Serial = "s",
                    Path = "/dev/sda",
                    PowerState = DevicePowerState.Standby
                }
            ]
        });
        using var client = await Support.OperatorClient.SignedIn(factory);

        var html = await client.GetStringAsync("/devices");

        Assert.That(html, Does.Contain("d0"));
        Assert.That(html, Does.Contain("cc-table"));
        Assert.That(html, Does.Not.Contain("No drives reported"));
    }

    [Test]
    public async Task Devices_page_lists_every_host_that_pushed()
    {
        using var factory = new Support.ControlAppFactory();
        var devices = factory.Services.GetRequiredService<DevicesController>();
        devices.ApplyObserved(Observation("node-a", "node-a-osd-1", 1));
        devices.ApplyObserved(Observation("node-b", "node-b-osd-2", 2));
        devices.ApplyObserved(Observation("node-c", "node-c-osd-0", 0));
        using var client = await Support.OperatorClient.SignedIn(factory);

        var html = await client.GetStringAsync("/devices");

        Assert.That(html, Does.Contain("node-a"));
        Assert.That(html, Does.Contain("node-b"));
        Assert.That(html, Does.Contain("node-c"));
        Assert.That(html, Does.Contain("node-a-osd-1"));
        Assert.That(html, Does.Contain("node-b-osd-2"));
        Assert.That(html, Does.Contain("node-c-osd-0"));
        Assert.That(html, Does.Not.Contain("No drives reported"));
    }

    [Test]
    public async Task Devices_page_does_not_drop_a_host_when_another_pushes()
    {
        using var factory = new Support.ControlAppFactory();
        var devices = factory.Services.GetRequiredService<DevicesController>();
        devices.ApplyObserved(Observation("node-a", "node-a-osd-1", 1));
        devices.ApplyObserved(Observation("node-b", "node-b-osd-2", 2));
        devices.ApplyObserved(Observation("node-a", "node-a-osd-1", 1));
        using var client = await Support.OperatorClient.SignedIn(factory);

        var html = await client.GetStringAsync("/devices");

        Assert.That(html, Does.Contain("node-a"));
        Assert.That(html, Does.Contain("node-b"));
        Assert.That(html, Does.Not.Contain("node-c-osd-0"));
    }

    private static HostDevicesObservationDto Observation(string hostId, string deviceId, int osdId)
        => new()
        {
            HostId = hostId,
            Devices =
            [
                new DeviceDto
                {
                    DeviceId = deviceId,
                    HostId = hostId,
                    MappedOsdId = osdId,
                    Wwn = deviceId,
                    Serial = deviceId,
                    Path = "/mnt/ramdisk/osd.img",
                    PowerState = DevicePowerState.Active
                }
            ]
        };
}

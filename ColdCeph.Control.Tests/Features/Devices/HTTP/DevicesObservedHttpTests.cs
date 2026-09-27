using System.Net;
using System.Net.Http.Json;
using ColdCeph.Control.Features.Devices.Controllers;
using ColdCeph.Control.Features.Hosts.Controllers;
using ColdCeph.Core.Features.Devices.DTOs;
using ColdCeph.Core.Features.Hosts.DTOs;
using Microsoft.Extensions.DependencyInjection;

namespace ColdCeph.Control.Tests.Features.Devices.HTTP;

[TestFixture]
public sealed class DevicesObservedHttpTests
{
    [Test]
    public async Task Observed_accepts_an_enrolled_host()
    {
        using var factory = new Support.ControlAppFactory();
        Enroll(factory, "dev");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-ColdCeph-Token", "changeme");

        var response = await client.PostAsJsonAsync("/v1/devices/observed", Observation("dev"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(factory.Services.GetRequiredService<DevicesController>().ListDevices().Select(device => device.DeviceId), Does.Contain("d0"));
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

        var response = await client.PostAsJsonAsync("/v1/devices/observed", Observation("dev"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(factory.Services.GetRequiredService<DevicesController>().ListDevices(), Is.Empty);
    }

    private static void Enroll(Support.ControlAppFactory factory, string hostId)
    {
        var hosts = factory.Services.GetRequiredService<HostsController>();
        hosts.RequestJoin(
            new NodeStatusDto { HostId = hostId, Hostname = hostId, ObservedAt = DateTimeOffset.UtcNow },
            "http://127.0.0.1:7080");
        hosts.Approve(hostId);
    }

    private static HostDevicesObservationDto Observation(string hostId)
        => new()
        {
            HostId = hostId,
            Devices =
            [
                new DeviceDto
                {
                    DeviceId = "d0",
                    HostId = hostId,
                    MappedOsdId = 0,
                    Wwn = "wwn",
                    Serial = "s",
                    Path = "/dev/sda",
                    PowerState = DevicePowerState.Standby
                }
            ]
        };
}

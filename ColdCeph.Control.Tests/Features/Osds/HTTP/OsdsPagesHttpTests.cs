using ColdCeph.Control.Features.Osds.Controllers;
using ColdCeph.Core.Features.Integrity.DTOs;
using ColdCeph.Core.Features.Osds.DTOs;
using Microsoft.Extensions.DependencyInjection;

namespace ColdCeph.Control.Tests.Features.Osds.HTTP;

[TestFixture]
public sealed class OsdsPagesHttpTests
{
    [Test]
    public async Task Osds_page_explains_when_no_node_has_pushed()
    {
        using var factory = new Support.ControlAppFactory();
        using var client = await Support.OperatorClient.SignedIn(factory);

        var html = await client.GetStringAsync("/osds");

        Assert.That(html, Does.Contain("No OSDs reported"));
        Assert.That(html, Does.Not.Contain("osd.0"));
    }

    [Test]
    public async Task Osds_page_lists_pushed_inventory()
    {
        using var factory = new Support.ControlAppFactory();
        factory.Services.GetRequiredService<OsdsController>().ApplyObserved(new HostOsdsObservationDto
        {
            HostId = "dev",
            Osds = [new OsdDto { OsdId = 0, HostId = "dev", DeviceId = "d0", Up = false, In = true, ProcessRunning = false }]
        });
        using var client = await Support.OperatorClient.SignedIn(factory);

        var html = await client.GetStringAsync("/osds");

        Assert.That(html, Does.Contain("osd.0"));
        Assert.That(html, Does.Not.Contain("No OSDs reported"));
    }

    [Test]
    public async Task Osds_page_lists_every_host_that_pushed()
    {
        using var factory = new Support.ControlAppFactory();
        var osds = factory.Services.GetRequiredService<OsdsController>();
        osds.ApplyObserved(Observation("node-a", 1));
        osds.ApplyObserved(Observation("node-b", 2));
        osds.ApplyObserved(Observation("node-c", 0));
        using var client = await Support.OperatorClient.SignedIn(factory);

        var html = await client.GetStringAsync("/osds");

        Assert.That(html, Does.Contain("node-a"));
        Assert.That(html, Does.Contain("node-b"));
        Assert.That(html, Does.Contain("node-c"));
        Assert.That(html, Does.Contain("osd.1"));
        Assert.That(html, Does.Contain("osd.2"));
        Assert.That(html, Does.Contain("osd.0"));
        Assert.That(html, Does.Not.Contain("No OSDs reported"));
    }

    [Test]
    public async Task Osds_page_does_not_drop_a_host_when_another_pushes()
    {
        using var factory = new Support.ControlAppFactory();
        var osds = factory.Services.GetRequiredService<OsdsController>();
        osds.ApplyObserved(Observation("node-a", 1));
        osds.ApplyObserved(Observation("node-b", 2));
        osds.ApplyObserved(Observation("node-a", 1));
        using var client = await Support.OperatorClient.SignedIn(factory);

        var html = await client.GetStringAsync("/osds");

        Assert.That(html, Does.Contain("node-a"));
        Assert.That(html, Does.Contain("node-b"));
        Assert.That(html, Does.Not.Contain("node-c"));
    }

    [Test]
    public async Task Osds_page_overlays_ceph_up_in_for_each_pushed_osd()
    {
        using var factory = new Support.ControlAppFactory();
        factory.Ceph.OsdMembership = new Dictionary<int, OsdMembershipDto>
        {
            [1] = new() { OsdId = 1, Up = true, In = true },
            [2] = new() { OsdId = 2, Up = false, In = true }
        };
        var osds = factory.Services.GetRequiredService<OsdsController>();
        osds.ApplyObserved(Observation("node-a", 1));
        osds.ApplyObserved(Observation("node-b", 2));
        using var client = await Support.OperatorClient.SignedIn(factory);

        var html = await client.GetStringAsync("/osds");

        Assert.That(html, Does.Contain("up / in"));
        Assert.That(html, Does.Contain("down / in"));
        Assert.That(factory.Ceph.MembershipCalls, Is.EqualTo(1));
        Assert.That(factory.Ceph.ObservationCalls, Is.EqualTo(0));
    }

    private static HostOsdsObservationDto Observation(string hostId, int osdId)
        => new()
        {
            HostId = hostId,
            Osds =
            [
                new OsdDto
                {
                    OsdId = osdId,
                    HostId = hostId,
                    DeviceId = $"{hostId}-osd-{osdId}",
                    Up = false,
                    In = true,
                    ProcessRunning = true
                }
            ]
        };
}

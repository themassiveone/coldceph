using ColdCeph.Node.Composition;
using ColdCeph.Node.Features.Hosts.Controllers;
using ColdCeph.Node.Features.Hosts.Interfaces;
using ColdCeph.Node.Features.Hosts.Services;
using ColdCeph.Core.Features.Hosts.DTOs;

namespace ColdCeph.Node.Tests.Features.Hosts.Unit;

[TestFixture]
public sealed class HostsHeartbeatLoopTests
{
    [Test]
    public void BeatOnce_posts_status_when_control_is_configured()
    {
        var client = new RecordingHeartbeatClient();
        var (loop, hosts) = Create(client);

        loop.BeatOnce();

        Assert.That(client.Calls, Has.Count.EqualTo(1));
        Assert.That(client.Calls[0].Status.HostId, Is.EqualTo("dev"));
        Assert.That(client.Calls[0].Advertise, Is.EqualTo(new Uri("http://127.0.0.1:7080")));
        Assert.That(hosts.IsControlEnrolled(), Is.True);
    }

    [Test]
    public void BeatOnce_does_not_post_when_control_is_unset()
    {
        var client = new RecordingHeartbeatClient();
        var config = new NodeConfig { HostId = "dev", Hostname = "dev" };
        var hosts = new HostsController(new HostsService(config));
        var loop = new HostsHeartbeatLoop(hosts, client, config);

        loop.BeatOnce();

        Assert.That(client.Calls, Is.Empty);
        Assert.That(hosts.IsControlEnrolled(), Is.False);
    }

    [Test]
    public void BeatOnce_does_not_enroll_on_pending_join()
    {
        var client = new RecordingHeartbeatClient { StatusCode = 202 };
        var (loop, hosts) = Create(client);

        loop.BeatOnce();

        Assert.That(hosts.IsControlEnrolled(), Is.False);
    }

    private static (HostsHeartbeatLoop Loop, HostsController Hosts) Create(RecordingHeartbeatClient client)
    {
        var config = new NodeConfig
        {
            ControlEndpoint = new Uri("http://127.0.0.1:8080"),
            AdvertiseEndpoint = new Uri("http://127.0.0.1:7080"),
            HostId = "dev",
            Hostname = "dev"
        };
        var hosts = new HostsController(new HostsService(config));
        return (new HostsHeartbeatLoop(hosts, client, config), hosts);
    }

    private sealed class RecordingHeartbeatClient : IControlHeartbeatClient
    {
        public List<(NodeStatusDto Status, Uri Advertise)> Calls { get; } = [];
        public int StatusCode { get; set; } = 200;

        public int Send(NodeStatusDto status, Uri advertiseEndpoint)
        {
            Calls.Add((status, advertiseEndpoint));
            return StatusCode;
        }
    }
}

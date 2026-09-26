using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Hosts.Services;
using ColdCeph.Control.Tests.Fake;
using ColdCeph.Core.Features.Hosts.DTOs;

namespace ColdCeph.Control.Tests.Features.Hosts.Unit;

[TestFixture]
public sealed class HostsServiceTests
{
    [Test]
    public void Heartbeat_marks_the_host_alive()
    {
        var clock = new FakeClock();
        var hosts = new HostsService(new ControlConfig { HeartbeatStaleAfter = TimeSpan.FromSeconds(30) }, clock);

        var host = hosts.RegisterHeartbeat(new NodeStatusDto { HostId = "h1", Hostname = "node", ObservedAt = clock.UtcNow }, new Uri("http://127.0.0.1:7080"));

        Assert.That(host.Alive, Is.True);
        Assert.That(hosts.ListHosts(), Has.Count.EqualTo(1));
    }

    [Test]
    public void Host_becomes_stale_after_the_heartbeat_window()
    {
        var clock = new FakeClock();
        var hosts = new HostsService(new ControlConfig { HeartbeatStaleAfter = TimeSpan.FromSeconds(30) }, clock);
        hosts.RegisterHeartbeat(new NodeStatusDto { HostId = "h1", Hostname = "node", ObservedAt = clock.UtcNow }, new Uri("http://127.0.0.1:7080"));
        clock.UtcNow = clock.UtcNow.AddMinutes(5);

        Assert.That(hosts.GetHost("h1")!.Alive, Is.False);
        Assert.That(hosts.GetHost("missing"), Is.Null);
    }

    [Test]
    public void Configured_endpoint_is_alive_without_a_heartbeat()
    {
        var clock = new FakeClock();
        var hosts = new HostsService(
            new ControlConfig
            {
                HeartbeatStaleAfter = TimeSpan.FromSeconds(30),
                ConfiguredNodeEndpoints = [new Uri("http://127.0.0.1:7080")],
                ConfiguredNodeHostId = "dev"
            },
            clock);

        Assert.That(hosts.GetHost("dev")!.Alive, Is.True);
        Assert.That(hosts.GetHost("dev")!.Endpoint, Is.EqualTo(new Uri("http://127.0.0.1:7080")));
    }

    [Test]
    public void Missing_configured_endpoint_does_not_invent_a_host()
    {
        var hosts = new HostsService(new ControlConfig { ConfiguredNodeEndpoints = [] }, new FakeClock());

        Assert.That(hosts.ListHosts(), Is.Empty);
        Assert.That(hosts.GetHost("dev"), Is.Null);
    }

    [Test]
    public void Join_request_waits_for_operator_approval()
    {
        var hosts = new HostsService(new ControlConfig(), new FakeClock());
        var status = new NodeStatusDto { HostId = "h1", Hostname = "node", ObservedAt = DateTimeOffset.UtcNow };

        var result = hosts.RequestJoin(status, "http://127.0.0.1:7080");

        Assert.That(result.StatusCode, Is.EqualTo(202));
        Assert.That(result.Host, Is.Null);
        Assert.That(hosts.ListHosts(), Is.Empty);
        Assert.That(hosts.ListPendingJoins(), Has.Count.EqualTo(1));
        Assert.That(hosts.GetHost("h1"), Is.Null);
    }

    [Test]
    public void Join_request_does_not_enroll_without_approval()
    {
        var hosts = new HostsService(new ControlConfig(), new FakeClock());
        var status = new NodeStatusDto { HostId = "h1", Hostname = "node", ObservedAt = DateTimeOffset.UtcNow };
        hosts.RequestJoin(status, "http://127.0.0.1:7080");
        hosts.RequestJoin(status, "http://127.0.0.1:7080");

        Assert.That(hosts.ListHosts(), Is.Empty);
        Assert.That(hosts.ListPendingJoins(), Has.Count.EqualTo(1));
    }

    [Test]
    public void Approve_enrolls_a_pending_node()
    {
        var hosts = new HostsService(new ControlConfig(), new FakeClock());
        hosts.RequestJoin(
            new NodeStatusDto { HostId = "h1", Hostname = "node", ObservedAt = DateTimeOffset.UtcNow },
            "http://127.0.0.1:7080");

        var enrolled = hosts.Approve("h1");

        Assert.That(enrolled!.Alive, Is.True);
        Assert.That(hosts.GetHost("h1")!.Endpoint, Is.EqualTo(new Uri("http://127.0.0.1:7080")));
        Assert.That(hosts.ListPendingJoins(), Is.Empty);
    }

    [Test]
    public void Approve_does_not_invent_an_unknown_host()
    {
        var hosts = new HostsService(new ControlConfig(), new FakeClock());

        Assert.That(hosts.Approve("missing"), Is.Null);
        Assert.That(hosts.ListHosts(), Is.Empty);
    }

    [Test]
    public void Denied_node_stays_off_the_enrolled_list()
    {
        var hosts = new HostsService(new ControlConfig(), new FakeClock());
        var status = new NodeStatusDto { HostId = "h1", Hostname = "node", ObservedAt = DateTimeOffset.UtcNow };
        hosts.RequestJoin(status, "http://127.0.0.1:7080");
        hosts.Deny("h1");

        var again = hosts.RequestJoin(status, "http://127.0.0.1:7080");

        Assert.That(again.StatusCode, Is.EqualTo(403));
        Assert.That(hosts.GetHost("h1"), Is.Null);
        Assert.That(hosts.ListPendingJoins(), Is.Empty);
        Assert.That(hosts.ListBlockedJoins(), Has.Count.EqualTo(1));
    }

    [Test]
    public void Deny_does_not_block_an_unknown_host()
    {
        var hosts = new HostsService(new ControlConfig(), new FakeClock());

        Assert.That(hosts.Deny("missing"), Is.False);
        Assert.That(hosts.ListBlockedJoins(), Is.Empty);
    }

    [Test]
    public void Approved_node_can_heartbeat_without_another_approval()
    {
        var hosts = new HostsService(new ControlConfig(), new FakeClock());
        var status = new NodeStatusDto { HostId = "h1", Hostname = "node", ObservedAt = DateTimeOffset.UtcNow };
        hosts.RequestJoin(status, "http://127.0.0.1:7080");
        hosts.Approve("h1");

        var result = hosts.RequestJoin(status, "http://127.0.0.1:7080");

        Assert.That(result.StatusCode, Is.EqualTo(200));
        Assert.That(hosts.ListHosts(), Has.Count.EqualTo(1));
        Assert.That(hosts.ListPendingJoins(), Is.Empty);
    }

    [Test]
    public void Join_request_rejects_a_missing_endpoint()
    {
        var hosts = new HostsService(new ControlConfig(), new FakeClock());
        var result = hosts.RequestJoin(
            new NodeStatusDto { HostId = "h1", Hostname = "h1", ObservedAt = DateTimeOffset.UtcNow },
            "not-a-url");

        Assert.That(result.StatusCode, Is.EqualTo(400));
        Assert.That(hosts.ListPendingJoins(), Is.Empty);
        Assert.That(hosts.GetHost("h1"), Is.Null);
    }
}

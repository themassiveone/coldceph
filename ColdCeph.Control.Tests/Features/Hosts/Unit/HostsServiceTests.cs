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

        var host = hosts.RegisterHeartbeat(new AgentStatusDto { HostId = "h1", Hostname = "node", ObservedAt = clock.UtcNow }, new Uri("http://127.0.0.1:7080"));

        Assert.That(host.Alive, Is.True);
        Assert.That(hosts.ListHosts(), Has.Count.EqualTo(1));
    }

    [Test]
    public void Host_becomes_stale_after_the_heartbeat_window()
    {
        var clock = new FakeClock();
        var hosts = new HostsService(new ControlConfig { HeartbeatStaleAfter = TimeSpan.FromSeconds(30) }, clock);
        hosts.RegisterHeartbeat(new AgentStatusDto { HostId = "h1", Hostname = "node", ObservedAt = clock.UtcNow }, new Uri("http://127.0.0.1:7080"));
        clock.UtcNow = clock.UtcNow.AddMinutes(5);

        Assert.That(hosts.GetHost("h1")!.Alive, Is.False);
        Assert.That(hosts.GetHost("missing"), Is.Null);
    }
}

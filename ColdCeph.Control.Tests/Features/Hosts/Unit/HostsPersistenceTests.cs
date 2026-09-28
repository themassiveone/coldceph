using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Hosts.Repositories;
using ColdCeph.Control.Features.Hosts.Services;
using ColdCeph.Control.Tests.Fake;
using ColdCeph.Core.Features.Hosts.DTOs;

namespace ColdCeph.Control.Tests.Features.Hosts.Unit;

[TestFixture]
public sealed class HostsPersistenceTests
{
    [Test]
    public void Approved_host_does_not_need_another_allow_after_reload()
    {
        var dir = Path.Join(Path.GetTempPath(), "coldceph-tests", Guid.NewGuid().ToString("N"));
        var config = new ControlConfig { DataDirectory = dir };
        var clock = new FakeClock();
        var status = new NodeStatusDto { HostId = "h1", Hostname = "node", ObservedAt = clock.UtcNow };
        var first = new HostsService(config, clock, new SqliteHostsRepository(config));
        first.RequestJoin(status, "http://127.0.0.1:7080");
        first.Approve("h1");

        var second = new HostsService(config, clock, new SqliteHostsRepository(config));
        var result = second.RequestJoin(status, "http://127.0.0.1:7080");

        Assert.That(result.StatusCode, Is.EqualTo(200));
        Assert.That(second.GetHost("h1")!.Endpoint, Is.EqualTo(new Uri("http://127.0.0.1:7080")));
        Assert.That(second.ListPendingJoins(), Is.Empty);
    }

    [Test]
    public void Pending_join_stays_pending_after_reload()
    {
        var dir = Path.Join(Path.GetTempPath(), "coldceph-tests", Guid.NewGuid().ToString("N"));
        var config = new ControlConfig { DataDirectory = dir };
        var clock = new FakeClock();
        var first = new HostsService(config, clock, new SqliteHostsRepository(config));
        first.RequestJoin(
            new NodeStatusDto { HostId = "h1", Hostname = "node", ObservedAt = clock.UtcNow },
            "http://127.0.0.1:7080");

        var second = new HostsService(config, clock, new SqliteHostsRepository(config));

        Assert.That(second.ListHosts(), Is.Empty);
        Assert.That(second.ListPendingJoins().Select(join => join.HostId), Does.Contain("h1"));
    }

    [Test]
    public void Denied_host_stays_blocked_after_reload()
    {
        var dir = Path.Join(Path.GetTempPath(), "coldceph-tests", Guid.NewGuid().ToString("N"));
        var config = new ControlConfig { DataDirectory = dir };
        var clock = new FakeClock();
        var status = new NodeStatusDto { HostId = "h1", Hostname = "node", ObservedAt = clock.UtcNow };
        var first = new HostsService(config, clock, new SqliteHostsRepository(config));
        first.RequestJoin(status, "http://127.0.0.1:7080");
        first.Deny("h1");

        var second = new HostsService(config, clock, new SqliteHostsRepository(config));
        var result = second.RequestJoin(status, "http://127.0.0.1:7080");

        Assert.That(result.StatusCode, Is.EqualTo(403));
        Assert.That(second.GetHost("h1"), Is.Null);
        Assert.That(second.ListBlockedJoins().Select(join => join.HostId), Does.Contain("h1"));
    }
}

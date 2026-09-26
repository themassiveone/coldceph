using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Hosts.Services;
using ColdCeph.Control.Tests.Fake;
using ColdCeph.Control.Tests.Support;

namespace ColdCeph.Control.Tests.Features.Hosts.Unit;

[TestFixture]
public sealed class ConfiguredNodeEndpointTests
{
    [Test]
    public void FromEnvironment_seeds_a_configured_node_endpoint()
    {
        using var endpoint = EnvScope.Set("COLDCEPH_NODE_ENDPOINT", "http://127.0.0.1:7080");
        using var hostId = EnvScope.Set("COLDCEPH_NODE_HOST_ID", "dev");
        using var cephBinary = EnvScope.Set("COLDCEPH_CEPH_BINARY", null);
        var clock = new FakeClock();
        var hosts = new HostsService(ControlConfig.FromEnvironment(), clock);

        var host = hosts.GetHost("dev");

        Assert.That(host, Is.Not.Null);
        Assert.That(host!.Endpoint, Is.EqualTo(new Uri("http://127.0.0.1:7080")));
        Assert.That(host.Alive, Is.True);
    }

    [Test]
    public void FromEnvironment_does_not_seed_hosts_when_the_endpoint_is_unset()
    {
        using var endpoint = EnvScope.Set("COLDCEPH_NODE_ENDPOINT", null);
        using var hostId = EnvScope.Set("COLDCEPH_NODE_HOST_ID", null);
        var hosts = new HostsService(ControlConfig.FromEnvironment(), new FakeClock());

        Assert.That(hosts.ListHosts(), Is.Empty);
        Assert.That(hosts.GetHost("dev"), Is.Null);
    }
}

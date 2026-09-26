using ColdCeph.Agent.Composition;
using ColdCeph.Agent.Tests.Support;

namespace ColdCeph.Agent.Tests.Features.Hosts.Unit;

[TestFixture]
public sealed class AgentConfigTests
{
    [Test]
    public void FromEnvironment_uses_configured_host_identity()
    {
        using var hostId = EnvScope.Set("COLDCEPH_HOST_ID", "dev");
        using var hostname = EnvScope.Set("COLDCEPH_HOSTNAME", "dev.local");
        using var token = EnvScope.Set("COLDCEPH_AGENT_TOKEN", "changeme");
        using var port = EnvScope.Set("AGENT_PORT", "7080");
        using var control = EnvScope.Set("COLDCEPH_CONTROL_ENDPOINT", null);

        var config = AgentConfig.FromEnvironment();

        Assert.That(config.HostId, Is.EqualTo("dev"));
        Assert.That(config.Hostname, Is.EqualTo("dev.local"));
        Assert.That(config.Port, Is.EqualTo(7080));
        Assert.That(config.AgentToken, Is.EqualTo("changeme"));
        Assert.That(config.ControlEndpoint, Is.Null);
        Assert.That(config.ListenUrls(), Does.Contain("http://*:7080"));
        Assert.That(config.ListenUrls(), Does.Not.Contain("http://127.0.0.1:7080"));
    }

    [Test]
    public void FromEnvironment_does_not_keep_a_blank_host_id()
    {
        using var hostId = EnvScope.Set("COLDCEPH_HOST_ID", "   ");
        using var hostname = EnvScope.Set("COLDCEPH_HOSTNAME", null);

        var config = AgentConfig.FromEnvironment();

        Assert.That(config.HostId, Is.Not.Empty);
        Assert.That(string.IsNullOrWhiteSpace(config.HostId), Is.False);
        Assert.That(config.HostId, Is.Not.EqualTo("   "));
    }

    [Test]
    public void ListenUrls_follows_the_configured_port()
    {
        using var port = EnvScope.Set("AGENT_PORT", "7999");

        var config = AgentConfig.FromEnvironment();

        Assert.That(config.ListenUrls(), Does.Contain("http://*:7999"));
        Assert.That(config.ListenUrls(), Does.Not.Contain("http://*:7080"));
    }

    [Test]
    public void FromEnvironment_reads_the_control_endpoint()
    {
        using var control = EnvScope.Set("COLDCEPH_CONTROL_ENDPOINT", "http://127.0.0.1:8080");
        using var advertise = EnvScope.Set("COLDCEPH_ADVERTISE_URL", "http://127.0.0.1:7080");

        var config = AgentConfig.FromEnvironment();

        Assert.That(config.ControlEndpoint, Is.EqualTo(new Uri("http://127.0.0.1:8080")));
        Assert.That(config.AdvertiseEndpoint, Is.EqualTo(new Uri("http://127.0.0.1:7080")));
    }
}

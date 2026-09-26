using ColdCeph.Node.Features.Hosts.Controllers;
using ColdCeph.Node.Features.Hosts.Services;
using ColdCeph.Node.Composition;

namespace ColdCeph.Node.Tests.Features.Hosts.Controller;

[TestFixture]
public sealed class HostsControllerTests
{
    [Test]
    public void GetStatus_returns_configured_identity()
    {
        var controller = new HostsController(new HostsService(new NodeConfig { HostId = "node-a", Hostname = "node-a.example" }));

        var status = controller.GetStatus();

        Assert.That(status.HostId, Is.EqualTo("node-a"));
        Assert.That(status.Hostname, Is.EqualTo("node-a.example"));
    }

    [Test]
    public void GetStatus_does_not_use_empty_host_id()
    {
        var controller = new HostsController(new HostsService(new NodeConfig { HostId = "node-b", Hostname = "node-b" }));

        Assert.That(controller.GetStatus().HostId, Is.Not.Empty);
        Assert.That(controller.GetStatus().HostId, Is.Not.EqualTo(""));
    }
}

using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Hosts.Controllers;
using ColdCeph.Control.Features.Hosts.Services;
using ColdCeph.Control.Features.Hosts.ViewModels;
using ColdCeph.Control.Tests.Fake;
using ColdCeph.Core.Features.Hosts.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace ColdCeph.Control.Tests.Features.Hosts.Controller;

[TestFixture]
public sealed class HostsPagesControllerTests
{
    [Test]
    public void Index_shows_pending_join_requests()
    {
        var page = CreatePage();
        page.Hosts.RequestJoin(
            new NodeStatusDto { HostId = "h1", Hostname = "node", ObservedAt = DateTimeOffset.UtcNow },
            "http://127.0.0.1:7080");

        var result = (ViewResult)page.Pages.Index();
        var model = (HostsPageViewModel)result.Model!;

        Assert.That(model.Pending, Has.Count.EqualTo(1));
        Assert.That(model.Enrolled, Is.Empty);
        Assert.That(model.Pending[0].HostId, Is.EqualTo("h1"));
    }

    [Test]
    public void Index_does_not_treat_a_pending_request_as_enrolled()
    {
        var page = CreatePage();
        page.Hosts.RequestJoin(
            new NodeStatusDto { HostId = "h1", Hostname = "node", ObservedAt = DateTimeOffset.UtcNow },
            "http://127.0.0.1:7080");

        var result = (ViewResult)page.Pages.Index();
        var model = (HostsPageViewModel)result.Model!;

        Assert.That(model.Enrolled, Is.Empty);
        Assert.That(model.Blocked, Is.Empty);
    }

    private static (HostsController Hosts, HostsPagesController Pages) CreatePage()
    {
        var hosts = new HostsController(new HostsService(new ControlConfig(), new FakeClock()));
        return (hosts, new HostsPagesController(hosts));
    }
}

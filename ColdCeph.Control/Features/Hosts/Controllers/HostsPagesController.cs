using ColdCeph.Control.Features.Hosts.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdCeph.Control.Features.Hosts.Controllers;

[Authorize]
public sealed class HostsPagesController : Controller
{
    private readonly HostsController _hosts;

    public HostsPagesController(HostsController hosts)
    {
        _hosts = hosts;
    }

    [HttpGet("/hosts")]
    public IActionResult Index()
        => View("Index", new HostsPageViewModel
        {
            Pending = _hosts.ListPendingJoins(),
            Enrolled = _hosts.ListHosts(),
            Blocked = _hosts.ListBlockedJoins()
        });

    [HttpPost("/hosts/{hostId}/approve")]
    [ValidateAntiForgeryToken]
    public IActionResult Approve(string hostId)
    {
        _hosts.Approve(hostId);
        return Redirect("/hosts");
    }

    [HttpPost("/hosts/{hostId}/deny")]
    [ValidateAntiForgeryToken]
    public IActionResult Deny(string hostId)
    {
        _hosts.Deny(hostId);
        return Redirect("/hosts");
    }
}

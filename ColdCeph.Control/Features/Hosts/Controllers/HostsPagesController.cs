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
    public IActionResult Index() => View("Index", _hosts.ListHosts());
}

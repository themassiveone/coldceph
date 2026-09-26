using Microsoft.AspNetCore.Mvc;

namespace ColdCeph.Node.Features.Hosts.Controllers;

[Route("v1")]
public sealed class HostsApiController : ControllerBase
{
    private readonly HostsController _hosts;

    public HostsApiController(HostsController hosts)
    {
        _hosts = hosts;
    }

    [HttpGet("status")]
    public IActionResult GetStatus() => Ok(_hosts.GetStatus());
}

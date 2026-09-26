using ColdCeph.Core.Features.Hosts.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdCeph.Control.Features.Hosts.Controllers;

[Authorize]
[Route("v1/hosts")]
public sealed class HostsApiController : ControllerBase
{
    private readonly HostsController _hosts;

    public HostsApiController(HostsController hosts)
    {
        _hosts = hosts;
    }

    [HttpGet]
    public IActionResult GetHosts() => Ok(_hosts.ListHosts());

    [HttpGet("{hostId}")]
    public IActionResult GetHost(string hostId)
    {
        var host = _hosts.GetHost(hostId);
        return host is null ? NotFound() : Ok(host);
    }

    [HttpPost("join")]
    [AllowAnonymous]
    public IActionResult Join([FromBody] NodeStatusDto status)
    {
        var result = _hosts.RequestJoin(status, Request.Headers["X-ColdCeph-Node-Endpoint"]);
        return StatusCode(result.StatusCode, result.Host);
    }
}

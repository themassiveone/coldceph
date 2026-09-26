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

    [HttpPost("heartbeat")]
    public IActionResult Heartbeat([FromBody] AgentStatusDto status)
        => Ok(_hosts.RegisterHeartbeat(status, AgentEndpoint(Request)));

    private static Uri AgentEndpoint(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-ColdCeph-Agent-Endpoint", out var value)
            && Uri.TryCreate(value.ToString(), UriKind.Absolute, out var uri))
            return uri;
        return new Uri("http://127.0.0.1:7080");
    }
}

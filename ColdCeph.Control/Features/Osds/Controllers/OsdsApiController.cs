using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Hosts.Controllers;
using ColdCeph.Control.Shared;
using ColdCeph.Core.Features.Osds.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdCeph.Control.Features.Osds.Controllers;

[Authorize]
[Route("v1/osds")]
public sealed class OsdsApiController : ControllerBase
{
    private readonly OsdsController _osds;
    private readonly HostsController _hosts;
    private readonly ControlConfig _config;

    public OsdsApiController(OsdsController osds, HostsController hosts, ControlConfig config)
    {
        _osds = osds;
        _hosts = hosts;
        _config = config;
    }

    [HttpGet]
    public IActionResult GetOsds() => Ok(_osds.ListOsds());

    [HttpGet("{id:int}")]
    public IActionResult GetOsd(int id)
    {
        var osd = _osds.GetOsd(id);
        return osd is null ? NotFound() : Ok(osd);
    }

    [AllowAnonymous]
    [HttpPost("observed")]
    public IActionResult Observed([FromBody] HostOsdsObservationDto observation)
    {
        var rejected = NodeObservationGate.Reject(Request, _config.NodeToken, _hosts, observation.HostId);
        if (rejected is not null)
            return rejected;
        _osds.ApplyObserved(observation);
        return Ok();
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdCeph.Control.Features.Osds.Controllers;

[Authorize]
[Route("v1/osds")]
public sealed class OsdsApiController : ControllerBase
{
    private readonly OsdsController _osds;

    public OsdsApiController(OsdsController osds)
    {
        _osds = osds;
    }

    [HttpGet]
    public IActionResult GetOsds() => Ok(_osds.ListOsds());

    [HttpGet("{id:int}")]
    public IActionResult GetOsd(int id)
    {
        var osd = _osds.GetOsd(id);
        return osd is null ? NotFound() : Ok(osd);
    }
}

using ColdCeph.Core.Features.Osds.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace ColdCeph.Node.Features.Osds.Controllers;

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

    [HttpPost("{id:int}/start")]
    public IActionResult Start(int id, [FromBody] OsdMutationRequest request)
        => Ok(_osds.Start(request with { OsdId = id, DesiredRunning = true }));

    [HttpPost("{id:int}/stop")]
    public IActionResult Stop(int id, [FromBody] OsdMutationRequest request)
        => Ok(_osds.Stop(request with { OsdId = id, DesiredRunning = false }));
}

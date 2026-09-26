using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdCeph.Control.Features.Integrity.Controllers;

[Authorize]
[Route("v1/cluster")]
public sealed class IntegrityApiController : ControllerBase
{
    private readonly IntegrityController _integrity;

    public IntegrityApiController(IntegrityController integrity)
    {
        _integrity = integrity;
    }

    [HttpGet("health")]
    public IActionResult GetHealth() => Ok(_integrity.GetIntegrity().Raw);

    [HttpGet("integrity")]
    public IActionResult GetIntegrityDocument() => Ok(_integrity.GetIntegrity());
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdCeph.Control.Features.Integrity.Controllers;

[Authorize]
public sealed class IntegrityPagesController : Controller
{
    private readonly IntegrityController _integrity;

    public IntegrityPagesController(IntegrityController integrity)
    {
        _integrity = integrity;
    }

    [HttpGet("/integrity")]
    public IActionResult Index() => View("Index", _integrity.GetIntegrity());
}

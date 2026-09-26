using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdCeph.Control.Features.Osds.Controllers;

[Authorize]
public sealed class OsdsPagesController : Controller
{
    private readonly OsdsController _osds;

    public OsdsPagesController(OsdsController osds)
    {
        _osds = osds;
    }

    [HttpGet("/osds")]
    public IActionResult Index() => View("Index", _osds.ListOsds());
}

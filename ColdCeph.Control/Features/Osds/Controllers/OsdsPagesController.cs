using ColdCeph.Control.Features.Osds.ViewModels;
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
    public IActionResult Index()
        => View("Index", new OsdsPageViewModel
        {
            Osds = _osds.ListOsds(),
            Errors = _osds.ListObservationErrors()
        });
}

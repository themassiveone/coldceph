using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdCeph.Control.Features.S3.Controllers;

[Authorize]
public sealed class S3PagesController : Controller
{
    private readonly S3Controller _s3;

    public S3PagesController(S3Controller s3)
    {
        _s3 = s3;
    }

    [HttpGet("/s3")]
    public IActionResult Index() => View("Index", _s3.GetPendingWork());
}

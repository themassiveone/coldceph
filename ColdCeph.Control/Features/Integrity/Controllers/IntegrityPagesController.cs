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
    public IActionResult Index() => Redirect("/");

    [HttpPost("/integrity/check")]
    [ValidateAntiForgeryToken]
    public IActionResult Check(string? returnUrl)
    {
        _integrity.GetIntegrity();
        return Redirect(SafeLocalUrl(returnUrl));
    }

    private static string SafeLocalUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)
            || !returnUrl.StartsWith("/", StringComparison.Ordinal)
            || returnUrl.StartsWith("//", StringComparison.Ordinal)
            || returnUrl.Contains('\\'))
            return "/";
        return returnUrl;
    }
}

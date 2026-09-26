using ColdCeph.Control.Features.Auth.Controllers;
using ColdCeph.Control.Features.StoragePlane.Services;
using ColdCeph.Control.Features.StoragePlane.ViewModels;
using ColdCeph.Core.Features.Operations.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdCeph.Control.Features.StoragePlane.Controllers;

[Authorize]
public sealed class StoragePlanePagesController : Controller
{
    private readonly StoragePlaneController _plane;
    private readonly AuthController _auth;

    public StoragePlanePagesController(StoragePlaneController plane, AuthController auth)
    {
        _plane = plane;
        _auth = auth;
    }

    [HttpGet("/")]
    public IActionResult Index() => View("Index", StoragePlanePageViewModel.From(_plane.GetState()));

    [HttpPost("/wake")]
    [ValidateAntiForgeryToken]
    public IActionResult Wake()
    {
        _plane.RequestWake(OperationIdRules.Create().Value, _auth.GetCurrentPrincipal().Name);
        return Redirect("/");
    }

    [HttpPost("/sleep")]
    [ValidateAntiForgeryToken]
    public IActionResult Sleep()
    {
        _plane.RequestSleep(OperationIdRules.Create().Value, _auth.GetCurrentPrincipal().Name);
        return Redirect("/");
    }
}

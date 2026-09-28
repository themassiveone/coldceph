using ColdCeph.Control.Features.Auth.Controllers;
using ColdCeph.Control.Features.Devices.Controllers;
using ColdCeph.Control.Features.Hosts.Controllers;
using ColdCeph.Control.Features.Integrity.Controllers;
using ColdCeph.Control.Features.Osds.Controllers;
using ColdCeph.Control.Features.S3.Controllers;
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
    private readonly IntegrityController _integrity;
    private readonly HostsController _hosts;
    private readonly DevicesController _devices;
    private readonly OsdsController _osds;
    private readonly S3Controller _s3;

    public StoragePlanePagesController(
        StoragePlaneController plane,
        AuthController auth,
        IntegrityController integrity,
        HostsController hosts,
        DevicesController devices,
        OsdsController osds,
        S3Controller s3)
    {
        _plane = plane;
        _auth = auth;
        _integrity = integrity;
        _hosts = hosts;
        _devices = devices;
        _osds = osds;
        _s3 = s3;
    }

    [HttpGet("/")]
    public IActionResult Index()
    {
        var hosts = _hosts.ListHosts();
        return View("Index", StoragePlanePageViewModel.From(
            _plane.GetState(),
            _integrity.GetLastIntegrity(),
            _s3.GetPendingWork(),
            hosts,
            _hosts.ListPendingJoins().Count,
            _devices.ListDevices(),
            _osds.ListObservedOsds()));
    }

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

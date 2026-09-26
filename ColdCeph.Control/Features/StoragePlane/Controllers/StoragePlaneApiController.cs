using ColdCeph.Control.Features.Auth.Controllers;
using ColdCeph.Core.Features.Operations.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdCeph.Control.Features.StoragePlane.Controllers;

[Authorize]
[Route("v1/cluster")]
public sealed class StoragePlaneApiController : ControllerBase
{
    private readonly StoragePlaneController _plane;
    private readonly AuthController _auth;

    public StoragePlaneApiController(StoragePlaneController plane, AuthController auth)
    {
        _plane = plane;
        _auth = auth;
    }

    [HttpGet("state")]
    public IActionResult GetClusterState() => Ok(_plane.GetState());

    [HttpPost("wake")]
    public IActionResult WakeCluster()
    {
        _plane.RequestWake(OperationIdRules.Create().Value, _auth.GetCurrentPrincipal().Name);
        return Accepted(_plane.GetLease());
    }

    [HttpPost("sleep")]
    public IActionResult SleepCluster()
    {
        _plane.RequestSleep(OperationIdRules.Create().Value, _auth.GetCurrentPrincipal().Name);
        return Accepted(_plane.GetState());
    }
}

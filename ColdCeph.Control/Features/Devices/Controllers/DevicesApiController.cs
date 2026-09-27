using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Hosts.Controllers;
using ColdCeph.Control.Shared;
using ColdCeph.Core.Features.Devices.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdCeph.Control.Features.Devices.Controllers;

[Authorize]
[Route("v1/devices")]
public sealed class DevicesApiController : ControllerBase
{
    private readonly DevicesController _devices;
    private readonly HostsController _hosts;
    private readonly ControlConfig _config;

    public DevicesApiController(DevicesController devices, HostsController hosts, ControlConfig config)
    {
        _devices = devices;
        _hosts = hosts;
        _config = config;
    }

    [HttpGet]
    public IActionResult GetDevices() => Ok(_devices.ListDevices());

    [HttpGet("{id}")]
    public IActionResult GetDevice(string id)
    {
        var device = _devices.GetDevice(id);
        return device is null ? NotFound() : Ok(device);
    }

    [AllowAnonymous]
    [HttpPost("observed")]
    public IActionResult Observed([FromBody] HostDevicesObservationDto observation)
    {
        var rejected = NodeObservationGate.Reject(Request, _config.NodeToken, _hosts, observation.HostId);
        if (rejected is not null)
            return rejected;
        _devices.ApplyObserved(observation);
        return Ok();
    }
}

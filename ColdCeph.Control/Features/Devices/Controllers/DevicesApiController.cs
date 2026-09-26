using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdCeph.Control.Features.Devices.Controllers;

[Authorize]
[Route("v1/devices")]
public sealed class DevicesApiController : ControllerBase
{
    private readonly DevicesController _devices;

    public DevicesApiController(DevicesController devices)
    {
        _devices = devices;
    }

    [HttpGet]
    public IActionResult GetDevices() => Ok(_devices.ListDevices());

    [HttpGet("{id}")]
    public IActionResult GetDevice(string id)
    {
        var device = _devices.GetDevice(id);
        return device is null ? NotFound() : Ok(device);
    }
}

using ColdCeph.Core.Features.Devices.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace ColdCeph.Node.Features.Devices.Controllers;

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

    [HttpPost("{id}/wake")]
    public IActionResult Wake(string id, [FromBody] DeviceMutationRequest request)
        => Ok(_devices.Wake(request with { DeviceId = id, DesiredPowerState = DevicePowerState.Active }));

    [HttpPost("{id}/standby")]
    public IActionResult Standby(string id, [FromBody] DeviceMutationRequest request)
        => Ok(_devices.Standby(request with { DeviceId = id, DesiredPowerState = DevicePowerState.Standby }));
}

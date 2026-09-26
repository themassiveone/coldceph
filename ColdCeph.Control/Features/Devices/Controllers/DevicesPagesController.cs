using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdCeph.Control.Features.Devices.Controllers;

[Authorize]
public sealed class DevicesPagesController : Controller
{
    private readonly DevicesController _devices;

    public DevicesPagesController(DevicesController devices)
    {
        _devices = devices;
    }

    [HttpGet("/devices")]
    public IActionResult Index() => View("Index", _devices.ListDevices());
}

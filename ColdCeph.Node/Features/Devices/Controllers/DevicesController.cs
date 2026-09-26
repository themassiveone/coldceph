using ColdCeph.Node.Features.Devices.Services;
using ColdCeph.Core.Features.Devices.DTOs;

namespace ColdCeph.Node.Features.Devices.Controllers;

public sealed class DevicesController
{
    private readonly DevicesService _service;

    public DevicesController(DevicesService service)
    {
        _service = service;
    }

    public IReadOnlyList<DeviceDto> ListDevices() => _service.ListDevices();

    public DeviceDto? GetDevice(string deviceId) => _service.GetDevice(deviceId);

    public DeviceMutationResult Wake(DeviceMutationRequest request) => _service.Wake(request);

    public DeviceMutationResult Standby(DeviceMutationRequest request) => _service.Standby(request);
}

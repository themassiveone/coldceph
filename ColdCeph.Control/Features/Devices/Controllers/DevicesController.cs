using ColdCeph.Control.Features.Devices.Services;
using ColdCeph.Core.Features.Devices.DTOs;

namespace ColdCeph.Control.Features.Devices.Controllers;

public sealed class DevicesController
{
    private readonly DevicesService _service;

    public DevicesController(DevicesService service)
    {
        _service = service;
    }

    public IReadOnlyList<DeviceDto> ListDevices() => _service.ListDevices();

    public DeviceDto? GetDevice(string deviceId) => _service.GetDevice(deviceId);

    public IReadOnlyList<string> ListObservationErrors() => _service.ListObservationErrors();

    public bool IsEveryDeviceStandby() => _service.IsEveryDeviceStandby();

    public void ApplyObserved(HostDevicesObservationDto observation) => _service.ApplyObserved(observation);
}

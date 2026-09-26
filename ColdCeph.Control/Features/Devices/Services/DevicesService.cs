using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Devices.Interfaces;
using ColdCeph.Core.Features.Devices.DTOs;

namespace ColdCeph.Control.Features.Devices.Services;

public sealed class DevicesService
{
    private readonly INodeDevicesClient _node;
    private readonly ControlConfig _config;
    private readonly Dictionary<string, DeviceDto> _devices = new(StringComparer.Ordinal);

    public DevicesService(INodeDevicesClient node, ControlConfig config)
    {
        _node = node;
        _config = config;
    }

    public IReadOnlyList<DeviceDto> ListDevices() => _devices.Values.ToArray();

    public DeviceDto? GetDevice(string deviceId)
        => _devices.TryGetValue(deviceId, out var device) ? device : null;

    public bool IsEveryDeviceStandby()
        => _devices.Values.All(device => device.PowerState == DevicePowerState.Standby);

    public void Seed(DeviceDto device) => _devices[device.DeviceId] = device;

    public void Observe(IEnumerable<DeviceDto> observed)
    {
        _devices.Clear();
        foreach (var device in observed)
            _devices[device.DeviceId] = device;
    }

    public void RefreshFromNode(Uri endpoint)
        => Observe(_node.List(endpoint));

    public void WakeAll(Uri nodeEndpoint, string operationId)
    {
        foreach (var device in _devices.Values.ToArray())
        {
            var result = _node.Wake(nodeEndpoint, new DeviceMutationRequest
            {
                DeviceId = device.DeviceId,
                OperationId = operationId,
                ControllerIdentity = _config.ControllerIdentity,
                Deadline = DateTimeOffset.UtcNow.AddMinutes(5),
                DesiredPowerState = DevicePowerState.Active
            });
            _devices[device.DeviceId] = device with { PowerState = result.PowerState };
        }
    }

    public void StandbyAll(Uri nodeEndpoint, string operationId)
    {
        foreach (var device in _devices.Values.ToArray())
        {
            var result = _node.Standby(nodeEndpoint, new DeviceMutationRequest
            {
                DeviceId = device.DeviceId,
                OperationId = operationId,
                ControllerIdentity = _config.ControllerIdentity,
                Deadline = DateTimeOffset.UtcNow.AddMinutes(5),
                DesiredPowerState = DevicePowerState.Standby
            });
            _devices[device.DeviceId] = device with { PowerState = result.PowerState };
        }
    }
}

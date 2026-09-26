using ColdCeph.Node.Composition;
using ColdCeph.Node.Features.Devices.Interfaces;
using ColdCeph.Node.Features.Osds.Controllers;
using ColdCeph.Core.Features.Devices.DTOs;

namespace ColdCeph.Node.Features.Devices.Services;

public sealed class DevicesService
{
    private readonly IDiskPower _power;
    private readonly OsdsController _osds;
    private readonly NodeConfig _config;
    private readonly Dictionary<string, DeviceDto> _devices = new(StringComparer.Ordinal);

    public DevicesService(IDiskPower power, OsdsController osds, NodeConfig config)
    {
        _power = power;
        _osds = osds;
        _config = config;
    }

    public IReadOnlyList<DeviceDto> ListDevices()
        => _devices.Values.Select(Refresh).ToArray();

    public DeviceDto? GetDevice(string deviceId)
        => _devices.TryGetValue(deviceId, out var device) ? Refresh(device) : null;

    public void Seed(DeviceDto device) => _devices[device.DeviceId] = device;

    public DeviceMutationResult Wake(DeviceMutationRequest request)
    {
        var current = Require(request.DeviceId);
        if (current.PowerState == DevicePowerState.Active)
            return new DeviceMutationResult(current.DeviceId, DevicePowerState.Active, true);
        _power.Wake(current.Path);
        _devices[current.DeviceId] = current with { PowerState = DevicePowerState.Active, HostId = _config.HostId };
        return new DeviceMutationResult(current.DeviceId, DevicePowerState.Active, false);
    }

    public DeviceMutationResult Standby(DeviceMutationRequest request)
    {
        var current = Require(request.DeviceId);
        if (current.MappedOsdId is int osdId)
        {
            var osd = _osds.GetOsd(osdId);
            if (osd?.ProcessRunning == true)
                throw new InvalidOperationException("Never standby a device under a running OSD.");
        }

        if (current.PowerState == DevicePowerState.Standby)
            return new DeviceMutationResult(current.DeviceId, DevicePowerState.Standby, true);

        _power.Standby(current.Path);
        _devices[current.DeviceId] = current with { PowerState = DevicePowerState.Standby, HostId = _config.HostId };
        return new DeviceMutationResult(current.DeviceId, DevicePowerState.Standby, false);
    }

    private DeviceDto Refresh(DeviceDto device)
        => device with
        {
            PowerState = _power.GetPowerState(device.DeviceId, device.Path),
            HostId = _config.HostId
        };

    private DeviceDto Require(string deviceId)
        => GetDevice(deviceId) ?? throw new InvalidOperationException($"Unknown device {deviceId}.");
}

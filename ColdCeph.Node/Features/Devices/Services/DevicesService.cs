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
    private readonly object _gate = new();

    public DevicesService(IDiskPower power, OsdsController osds, NodeConfig config)
    {
        _power = power;
        _osds = osds;
        _config = config;
    }

    public IReadOnlyList<DeviceDto> ListDevices()
    {
        lock (_gate)
        {
            SyncFromOsds();
            return _devices.Values.Select(Refresh).ToArray();
        }
    }

    public DeviceDto? GetDevice(string deviceId)
    {
        lock (_gate)
        {
            SyncFromOsds();
            return _devices.TryGetValue(deviceId, out var device) ? Refresh(device) : null;
        }
    }

    public void Seed(DeviceDto device)
    {
        lock (_gate)
            _devices[device.DeviceId] = device;
    }

    public DeviceMutationResult Wake(DeviceMutationRequest request)
    {
        lock (_gate)
        {
            var current = Require(request.DeviceId);
            if (current.PowerState == DevicePowerState.Active)
                return new DeviceMutationResult(current.DeviceId, DevicePowerState.Active, true);
            _power.Wake(current.Path);
            _devices[current.DeviceId] = current with { PowerState = DevicePowerState.Active, HostId = _config.HostId };
            return new DeviceMutationResult(current.DeviceId, DevicePowerState.Active, false);
        }
    }

    public DeviceMutationResult Standby(DeviceMutationRequest request)
    {
        lock (_gate)
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
    }

    private void SyncFromOsds()
    {
        if (string.IsNullOrWhiteSpace(_config.OsdContainer))
            return;
        foreach (var osd in _osds.ListOsds())
        {
            var deviceId = osd.DeviceId ?? $"{_config.HostId}-osd-{osd.OsdId}";
            if (_devices.ContainsKey(deviceId))
                continue;
            _devices[deviceId] = new DeviceDto
            {
                DeviceId = deviceId,
                HostId = _config.HostId,
                MappedOsdId = osd.OsdId,
                Wwn = deviceId,
                Serial = deviceId,
                Path = "/mnt/ramdisk/osd.img",
                PowerState = DevicePowerState.Active
            };
        }
    }

    private DeviceDto Refresh(DeviceDto device)
        => device with
        {
            PowerState = _power.GetPowerState(device.DeviceId, device.Path),
            HostId = _config.HostId
        };

    private DeviceDto Require(string deviceId)
    {
        SyncFromOsds();
        return _devices.TryGetValue(deviceId, out var device)
            ? Refresh(device)
            : throw new InvalidOperationException($"Unknown device {deviceId}.");
    }
}

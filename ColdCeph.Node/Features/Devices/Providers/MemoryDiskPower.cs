using ColdCeph.Core.Features.Devices.DTOs;
using ColdCeph.Node.Features.Devices.Interfaces;

namespace ColdCeph.Node.Features.Devices.Providers;

public sealed class MemoryDiskPower : IDiskPower
{
    private readonly Dictionary<string, DevicePowerState> _states = new(StringComparer.Ordinal);

    public DevicePowerState GetPowerState(string deviceId, string? path)
        => _states.TryGetValue(Key(deviceId, path), out var state) ? state : DevicePowerState.Active;

    public void Wake(string? path)
        => _states[Key("", path)] = DevicePowerState.Active;

    public void Standby(string? path)
        => _states[Key("", path)] = DevicePowerState.Standby;

    private static string Key(string deviceId, string? path)
        => string.IsNullOrWhiteSpace(path) ? deviceId : path;
}

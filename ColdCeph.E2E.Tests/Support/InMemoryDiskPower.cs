using ColdCeph.Agent.Features.Devices.Interfaces;
using ColdCeph.Core.Features.Devices.DTOs;

namespace ColdCeph.E2E.Tests.Support;

public sealed class InMemoryDiskPower : IDiskPower
{
    private DevicePowerState _state = DevicePowerState.Standby;

    public DevicePowerState GetPowerState(string deviceId, string? path) => _state;

    public void Wake(string? path) => _state = DevicePowerState.Active;

    public void Standby(string? path) => _state = DevicePowerState.Standby;
}

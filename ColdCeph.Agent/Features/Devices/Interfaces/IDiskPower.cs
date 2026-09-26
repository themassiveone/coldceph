using ColdCeph.Core.Features.Devices.DTOs;

namespace ColdCeph.Agent.Features.Devices.Interfaces;

public interface IDiskPower
{
    DevicePowerState GetPowerState(string deviceId, string? path);
    void Wake(string? path);
    void Standby(string? path);
}

using ColdCeph.Node.Features.Devices.Interfaces;
using ColdCeph.Node.Shared;
using ColdCeph.Core.Features.Devices.DTOs;

namespace ColdCeph.Node.Features.Devices.Providers;

public sealed class HdparmDiskPower : IDiskPower
{
    private readonly IProcessRunner _runner;

    public HdparmDiskPower(IProcessRunner runner)
    {
        _runner = runner;
    }

    public DevicePowerState GetPowerState(string deviceId, string? path)
    {
        if (path is null)
            return DevicePowerState.Unknown;
        try
        {
            var output = _runner.Run("hdparm", ["-C", path]);
            if (output.Contains("standby", StringComparison.OrdinalIgnoreCase))
                return DevicePowerState.Standby;
            return DevicePowerState.Active;
        }
        catch (InvalidOperationException)
        {
            return DevicePowerState.Unknown;
        }
    }

    public void Wake(string? path)
    {
        if (path is null)
            return;
        _runner.Run("hdparm", ["-S", "0", path]);
    }

    public void Standby(string? path)
    {
        if (path is null)
            return;
        _runner.Run("hdparm", ["-y", path]);
    }
}

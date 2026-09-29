using ColdCeph.Node.Features.Devices.Interfaces;
using ColdCeph.Node.Shared;
using ColdCeph.Core.Features.Devices.DTOs;

namespace ColdCeph.Node.Features.Devices.Providers;

/// <summary>
/// Reads and changes a drive's power state with <c>hdparm</c>.
/// </summary>
public sealed class HdparmDiskPower : IDiskPower
{
    private readonly IProcessRunner _runner;

    public HdparmDiskPower(IProcessRunner runner)
    {
        _runner = runner;
    }

    /// <summary>
    /// <c>hdparm -C</c> prints "drive state is: &lt;state&gt;". Anything not recognised is
    /// <see cref="DevicePowerState.Unknown"/>, never <see cref="DevicePowerState.Active"/>:
    /// guessing Active means the plane concludes a spun-down disk is awake, and guessing it for
    /// <c>sleeping</c> in particular meant "every device is in standby" could never become true,
    /// so the appliance never finished going to sleep.
    /// </summary>
    public DevicePowerState GetPowerState(string deviceId, string? path)
    {
        if (path is null)
            return DevicePowerState.Unknown;

        string output;
        try
        {
            output = _runner.Run("hdparm", ["-C", path]);
        }
        catch (Exception)
        {
            return DevicePowerState.Unknown;
        }

        var state = ReadDriveState(output);
        return state switch
        {
            // standby and sleeping are both spun down. sleeping needs a reset to wake, but for
            // ColdCeph's purposes the platters are stopped either way.
            "standby" => DevicePowerState.Standby,
            "sleeping" => DevicePowerState.Standby,
            "active/idle" => DevicePowerState.Active,
            "active" => DevicePowerState.Active,
            "idle" => DevicePowerState.Active,
            _ => DevicePowerState.Unknown
        };
    }

    /// <summary>
    /// A drive spins up on I/O, not on a configuration change: <c>hdparm -S 0</c> only disables
    /// the spindown timer and leaves a standby drive standing by. So this reads a sector to force
    /// the spin-up, then clears the timer so it does not immediately drop back.
    /// </summary>
    public void Wake(string? path)
    {
        if (path is null)
            return;
        _runner.Run("hdparm", ["--read-sector", "0", path]);
        _runner.Run("hdparm", ["-S", "0", path]);
    }

    public void Standby(string? path)
    {
        if (path is null)
            return;
        _runner.Run("hdparm", ["-y", path]);
    }

    private static string ReadDriveState(string output)
    {
        foreach (var line in output.Split('\n', StringSplitOptions.TrimEntries))
        {
            var marker = line.IndexOf("drive state is:", StringComparison.OrdinalIgnoreCase);
            if (marker < 0)
                continue;
            return line[(marker + "drive state is:".Length)..].Trim().ToLowerInvariant();
        }

        return string.Empty;
    }
}

using ColdCeph.Control.Features.Devices.Interfaces;
using ColdCeph.Core.Features.Devices.DTOs;

namespace ColdCeph.Control.Tests.Fake;

/// <summary>
/// Stands in for a node's disk endpoint, including the case where a wake or standby is accepted
/// but the drive has not changed state yet.
/// </summary>
public sealed class FakeNodeDevicesClient : INodeDevicesClient
{
    private readonly NodeCommandLog _log;

    public FakeNodeDevicesClient(NodeCommandLog? log = null)
    {
        _log = log ?? new NodeCommandLog();
    }

    public IReadOnlyList<string> Commands => _log.Commands;

    public bool ThrowOnWake { get; set; }

    public HashSet<Uri> ThrowOnWakeFor { get; } = [];

    /// <summary>The drive is still spinning up.</summary>
    public bool WakeDoesNotTake { get; set; }

    public bool StandbyDoesNotTake { get; set; }

    public DeviceMutationResult Wake(Uri endpoint, DeviceMutationRequest request)
    {
        if (ThrowOnWake || ThrowOnWakeFor.Contains(endpoint))
            throw new InvalidOperationException("node down");
        _log.Add($"{endpoint.Port} wake {request.DeviceId}");
        return new DeviceMutationResult(
            request.DeviceId,
            WakeDoesNotTake ? DevicePowerState.Standby : DevicePowerState.Active,
            false);
    }

    public DeviceMutationResult Standby(Uri endpoint, DeviceMutationRequest request)
    {
        _log.Add($"{endpoint.Port} standby {request.DeviceId}");
        return new DeviceMutationResult(
            request.DeviceId,
            StandbyDoesNotTake ? DevicePowerState.Active : DevicePowerState.Standby,
            false);
    }
}

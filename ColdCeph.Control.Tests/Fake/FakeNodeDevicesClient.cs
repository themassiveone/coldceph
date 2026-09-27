using ColdCeph.Control.Features.Devices.Interfaces;
using ColdCeph.Core.Features.Devices.DTOs;

namespace ColdCeph.Control.Tests.Fake;

public sealed class FakeNodeDevicesClient : INodeDevicesClient
{
    public List<string> Commands { get; } = [];
    public DevicePowerState Power { get; set; } = DevicePowerState.Standby;

    public bool ThrowOnWake { get; set; }

    public HashSet<Uri> ThrowOnWakeFor { get; } = [];

    public DeviceMutationResult Wake(Uri endpoint, DeviceMutationRequest request)
    {
        if (ThrowOnWake || ThrowOnWakeFor.Contains(endpoint))
            throw new InvalidOperationException("node down");
        Commands.Add($"{endpoint.Port} wake {request.DeviceId}");
        Power = DevicePowerState.Active;
        return new DeviceMutationResult(request.DeviceId, DevicePowerState.Active, false);
    }

    public DeviceMutationResult Standby(Uri endpoint, DeviceMutationRequest request)
    {
        Commands.Add($"{endpoint.Port} standby {request.DeviceId}");
        Power = DevicePowerState.Standby;
        return new DeviceMutationResult(request.DeviceId, DevicePowerState.Standby, false);
    }
}

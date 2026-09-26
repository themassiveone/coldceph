using ColdCeph.Control.Features.Devices.Interfaces;
using ColdCeph.Core.Features.Devices.DTOs;

namespace ColdCeph.Control.Tests.Fake;

public sealed class FakeAgentDevicesClient : IAgentDevicesClient
{
    public List<string> Commands { get; } = [];
    public DevicePowerState Power { get; set; } = DevicePowerState.Standby;

    public List<DeviceDto> Inventory { get; set; } = [];

    public bool ThrowOnList { get; set; }

    public IReadOnlyList<DeviceDto> List(Uri endpoint)
    {
        if (ThrowOnList)
            throw new InvalidOperationException("agent down");
        return Inventory;
    }

    public DeviceMutationResult Wake(Uri endpoint, DeviceMutationRequest request)
    {
        Commands.Add($"wake {request.DeviceId}");
        Power = DevicePowerState.Active;
        return new DeviceMutationResult(request.DeviceId, DevicePowerState.Active, false);
    }

    public DeviceMutationResult Standby(Uri endpoint, DeviceMutationRequest request)
    {
        Commands.Add($"standby {request.DeviceId}");
        Power = DevicePowerState.Standby;
        return new DeviceMutationResult(request.DeviceId, DevicePowerState.Standby, false);
    }
}

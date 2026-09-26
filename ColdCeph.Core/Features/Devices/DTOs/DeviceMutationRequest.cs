namespace ColdCeph.Core.Features.Devices.DTOs;

public sealed record DeviceMutationRequest
{
    public required string DeviceId { get; init; }
    public required string OperationId { get; init; }
    public required string ControllerIdentity { get; init; }
    public required DateTimeOffset Deadline { get; init; }
    public required DevicePowerState DesiredPowerState { get; init; }
}

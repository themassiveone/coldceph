namespace ColdCeph.Core.Features.Devices.DTOs;

public sealed record DeviceMutationResult(string DeviceId, DevicePowerState PowerState, bool IdempotentHit);

namespace ColdCeph.Core.Features.StoragePlane.DTOs;

public sealed record IdlePolicyDto(TimeSpan IdleTimeout, bool SleepEnabled);

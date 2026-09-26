namespace ColdCeph.Core.Features.Integrity.DTOs;

public sealed record ReadinessPredicates
{
    public required bool ControlPlaneAvailable { get; init; }
    public required bool OsdPlaneExpected { get; init; }
    public required bool ReadReady { get; init; }
    public required bool WriteReady { get; init; }
    public required bool SleepSafe { get; init; }
}

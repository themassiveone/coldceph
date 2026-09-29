namespace ColdCeph.Core.Features.Integrity.DTOs;

public sealed record IntegritySnapshot
{
    public required CephHealthRaw Raw { get; init; }
    public required IReadOnlyList<ClassifiedHealthCheck> Checks { get; init; }
    public required ReadinessPredicates Predicates { get; init; }

    /// <summary>
    /// True when this confirmation found unfound objects, inconsistent PGs or incomplete
    /// PGs. StoragePlane reads this to decide FAULTED; it is never inferred from prose.
    /// </summary>
    public required bool DurabilityFailure { get; init; }

    public required ClusterCapacityDto? Capacity { get; init; }
    public required string? CapacityUnavailableReason { get; init; }
    public required DateTimeOffset? LastVerifiedCleanAt { get; init; }
    public required string? LastVerifiedCleanSummary { get; init; }
    public required DateTimeOffset ObservedAt { get; init; }
}

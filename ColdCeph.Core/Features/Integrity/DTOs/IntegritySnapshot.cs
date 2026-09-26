namespace ColdCeph.Core.Features.Integrity.DTOs;

public sealed record IntegritySnapshot
{
    public required CephHealthRaw Raw { get; init; }
    public required IReadOnlyList<ClassifiedHealthCheck> Checks { get; init; }
    public required ReadinessPredicates Predicates { get; init; }
    public required DateTimeOffset? LastVerifiedCleanAt { get; init; }
    public required string? LastVerifiedCleanSummary { get; init; }
    public required DateTimeOffset ObservedAt { get; init; }
}

namespace ColdCeph.Core.Features.Integrity.DTOs;

/// <summary>
/// Everything one Ceph confirmation reads, gathered by a single pass over the monitor.
/// The provider returns this record so "one confirmation is one set of CLI calls" is a
/// structural property rather than something a cache has to arrange.
/// </summary>
public sealed record CephObservation
{
    public required CephHealthRaw Health { get; init; }
    public required IReadOnlyList<CephHealthCheck> HealthChecks { get; init; }
    public required bool QuorumAvailable { get; init; }
    public required IReadOnlyList<PgStateCount> PgStates { get; init; }
    public required ClusterCapacityDto? Capacity { get; init; }
    public required string? CapacityUnavailableReason { get; init; }

    public static CephObservation Unavailable(string reason)
        => new()
        {
            Health = new CephHealthRaw { Status = "UNAVAILABLE", Summary = reason, Checks = [] },
            HealthChecks = [],
            QuorumAvailable = false,
            PgStates = [],
            Capacity = null,
            CapacityUnavailableReason = reason
        };
}

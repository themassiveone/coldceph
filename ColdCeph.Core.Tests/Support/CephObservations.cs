using ColdCeph.Core.Features.Integrity.DTOs;

namespace ColdCeph.Core.Tests.Support;

/// <summary>
/// Builds observations for the pure signal arithmetic in <see cref="CephSignals"/>.
/// <para>
/// These take Ceph's <em>identifiers</em> — health check names and PG state tokens — which are
/// a closed, stable vocabulary. Parsing Ceph's prose JSON into those identifiers is a separate
/// concern, covered in <c>ColdCeph.Control.Tests</c> against the captured fixture corpus.
/// </para>
/// </summary>
internal static class CephObservations
{
    public static CephObservation With(
        string status = "HEALTH_OK",
        IEnumerable<string>? checkNames = null,
        IEnumerable<string>? pgStates = null,
        bool quorum = true,
        string? checkMessage = null)
    {
        var checks = (checkNames ?? [])
            .Select(name => new CephHealthCheck
            {
                Name = name,
                Severity = status,
                Message = checkMessage ?? string.Empty
            })
            .ToArray();

        return new CephObservation
        {
            Health = new CephHealthRaw
            {
                Status = status,
                Summary = checks.Length == 0 ? status : string.Join("; ", checks.Select(check => check.Display)),
                Checks = checks.Select(check => check.Display).ToArray()
            },
            HealthChecks = checks,
            QuorumAvailable = quorum,
            PgStates = (pgStates ?? ["active+clean"])
                .Select(state => new PgStateCount { StateName = state, Count = 1 })
                .ToArray(),
            Capacity = new ClusterCapacityDto { TotalBytes = 3000, UsedBytes = 1000, AvailableBytes = 2000 },
            CapacityUnavailableReason = null
        };
    }

    public static IntegritySnapshot Snapshot(bool durabilityFailure, params string[] checkNames)
        => new()
        {
            Raw = new CephHealthRaw
            {
                Status = durabilityFailure ? "HEALTH_ERR" : "HEALTH_OK",
                Summary = string.Join("; ", checkNames),
                Checks = checkNames
            },
            Checks = checkNames
                .Select(name => new ClassifiedHealthCheck
                {
                    Name = name,
                    Detail = name,
                    Classification = HealthClassification.Unexpected,
                    Durability = durabilityFailure
                })
                .ToArray(),
            Predicates = CephSignals.Unavailable.ToPredicates(osdPlaneExpected: false),
            DurabilityFailure = durabilityFailure,
            Capacity = null,
            CapacityUnavailableReason = null,
            LastVerifiedCleanAt = null,
            LastVerifiedCleanSummary = null,
            ObservedAt = DateTimeOffset.UnixEpoch
        };
}

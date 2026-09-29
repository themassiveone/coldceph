namespace ColdCeph.Core.Features.Integrity.DTOs;

/// <summary>
/// The durability and readiness signals a Ceph confirmation carries, derived from
/// <see cref="CephObservation"/> by reading Ceph's stable identifiers: health check
/// <em>names</em> and PG <em>state tokens</em>.
/// <para>
/// This type deliberately does no substring matching over health messages. Ceph's
/// messages are prose ("1/3 objects unfound (33.333%)") and change between releases,
/// so a grep over them both misses real failures and invents false ones — and a false
/// positive here drives an irreversible FAULTED.
/// </para>
/// </summary>
public sealed record CephSignals
{
    public required bool QuorumAvailable { get; init; }
    public required bool PgsActive { get; init; }
    public required bool PgsClean { get; init; }
    public required bool HasUnfound { get; init; }
    public required bool HasInconsistent { get; init; }
    public required bool HasIncomplete { get; init; }
    public required bool HasStaleOrDown { get; init; }
    public required bool HasRecoveryOrBackfill { get; init; }
    public required bool HasFullOsds { get; init; }

    /// <summary>
    /// The three conditions spec §52 calls unexpected integrity: unfound objects,
    /// inconsistent PGs, incomplete PGs. Reaching FAULTED requires an external
    /// confirmation as well; this only says the confirmation found a durability failure.
    /// </summary>
    public bool DurabilityFailure => HasUnfound || HasInconsistent || HasIncomplete;

    public static readonly CephSignals Unavailable = new()
    {
        QuorumAvailable = false,
        PgsActive = false,
        PgsClean = false,
        HasUnfound = false,
        HasInconsistent = false,
        HasIncomplete = false,
        HasStaleOrDown = false,
        HasRecoveryOrBackfill = false,
        HasFullOsds = false
    };

    public static CephSignals From(CephObservation observation)
    {
        var names = observation.HealthChecks.Select(check => check.Name).ToArray();
        var pgs = observation.PgStates;

        return new CephSignals
        {
            QuorumAvailable = observation.QuorumAvailable,

            // A confirmation that reports no PGs at all tells us nothing about
            // availability, so it must not read as ready. `All` over an empty set is
            // true, which is why both of these carry an explicit non-empty guard.
            PgsActive = pgs.Count > 0 && pgs.All(pg => pg.HasToken("active")),
            PgsClean = pgs.Count > 0 && pgs.All(pg => pg.HasToken("clean")),

            HasUnfound = names.Intersect(UnfoundChecks, StringComparer.Ordinal).Any()
                         || AnyPgToken(pgs, UnfoundPgStates),
            HasInconsistent = names.Intersect(InconsistentChecks, StringComparer.Ordinal).Any()
                              || AnyPgToken(pgs, ["inconsistent"]),
            HasIncomplete = AnyPgToken(pgs, ["incomplete"]),
            HasStaleOrDown = AnyPgToken(pgs, ["stale", "down"]),
            HasRecoveryOrBackfill = AnyPgToken(pgs, RecoveryPgStates),
            HasFullOsds = names.Intersect(FullChecks, StringComparer.Ordinal).Any()
        };
    }

    /// <summary>
    /// Readiness arithmetic. <paramref name="osdPlaneExpected"/> comes from health
    /// classification, which needs the StoragePlane state and therefore stays in Control.
    /// Everything else is a function of the confirmation alone.
    /// </summary>
    public ReadinessPredicates ToPredicates(bool osdPlaneExpected)
    {
        var control = QuorumAvailable && osdPlaneExpected;
        var readReady = control && PgsActive && !HasStaleOrDown && !HasUnfound && !HasIncomplete;
        var writeReady = readReady && PgsClean && !HasInconsistent && !HasRecoveryOrBackfill && !HasFullOsds;
        var sleepSafe = control && PgsClean && !DurabilityFailure && !HasRecoveryOrBackfill && !HasFullOsds;
        return new ReadinessPredicates
        {
            ControlPlaneAvailable = QuorumAvailable,
            OsdPlaneExpected = osdPlaneExpected,
            ReadReady = readReady,
            WriteReady = writeReady,
            SleepSafe = sleepSafe
        };
    }

    private static bool AnyPgToken(IReadOnlyList<PgStateCount> pgs, IReadOnlyList<string> tokens)
        => pgs.Any(pg => tokens.Any(pg.HasToken));

    // Ceph health check names. Stable identifiers, unlike the messages beside them.
    private static readonly string[] UnfoundChecks = ["OBJECT_UNFOUND"];

    private static readonly string[] InconsistentChecks = ["PG_DAMAGED", "OSD_SCRUB_ERRORS"];

    private static readonly string[] FullChecks =
    [
        "OSD_FULL",
        "OSD_BACKFILLFULL",
        "OSD_NEARFULL",
        "POOL_FULL",
        "POOL_NEAR_FULL",
        "POOL_BACKFILLFULL",
        "PG_BACKFILL_FULL"
    ];

    private static readonly string[] UnfoundPgStates = ["recovery_unfound", "backfill_unfound"];

    private static readonly string[] RecoveryPgStates =
    [
        "recovering",
        "recovery_wait",
        "backfilling",
        "backfill_wait",
        "backfill_toofull"
    ];
}

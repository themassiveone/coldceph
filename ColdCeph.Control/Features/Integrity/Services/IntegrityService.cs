using ColdCeph.Control.Features.Integrity.Interfaces;
using ColdCeph.Control.Features.StoragePlane.Controllers;
using ColdCeph.Control.Shared;
using ColdCeph.Core.Features.Integrity.DTOs;
using ColdCeph.Core.Features.StoragePlane.DTOs;

namespace ColdCeph.Control.Features.Integrity.Services;

public sealed class IntegrityService
{
    private readonly ICephQueryProvider _ceph;
    private readonly IIntegrityRepository _repository;
    private readonly StoragePlaneController _plane;
    private readonly IClock _clock;

    public IntegrityService(
        ICephQueryProvider ceph,
        IIntegrityRepository repository,
        StoragePlaneController plane,
        IClock clock)
    {
        _ceph = ceph;
        _repository = repository;
        _plane = plane;
        _clock = clock;
    }

    private readonly object _snapshotGate = new();
    private IntegritySnapshot? _last;

    public CephHealthRaw GetRawHealth() => GetLastIntegrity().Raw;

    public IntegritySnapshot GetLastIntegrity()
    {
        lock (_snapshotGate)
            return _last ?? Unavailable("Open Integrity to read Ceph health.");
    }

    /// <summary>
    /// Returns an empty overlay when Ceph cannot be reached, and says so through
    /// <paramref name="unavailableReason"/> so callers can tell "the monitor did not answer"
    /// from "this cluster has no OSDs".
    /// </summary>
    public IReadOnlyDictionary<int, OsdMembershipDto> ListOsdMembership(out string? unavailableReason)
    {
        try
        {
            unavailableReason = null;
            return _ceph.ListOsdMembership();
        }
        catch (Exception exception)
        {
            unavailableReason = exception.Message;
            return new Dictionary<int, OsdMembershipDto>();
        }
    }

    public IReadOnlyDictionary<int, OsdMembershipDto> ListOsdMembership()
        => ListOsdMembership(out _);

    public IntegritySnapshot GetIntegrity()
    {
        CephObservation observation;
        try
        {
            observation = _ceph.GetObservation();
        }
        catch (Exception exception)
        {
            observation = CephObservation.Unavailable(exception.Message);
        }

        var snapshot = Interpret(observation);
        lock (_snapshotGate)
            _last = snapshot;
        return snapshot;
    }

    private IntegritySnapshot Interpret(CephObservation observation)
    {
        if (string.Equals(observation.Health.Status, "UNAVAILABLE", StringComparison.Ordinal))
            return Unavailable(observation.Health.Summary);

        var signals = CephSignals.From(observation);
        var plane = _plane.GetState().State;
        var ownedNoout = _plane.ListOwnedNoout();
        var checks = Classify(observation, signals, plane, ownedNoout);
        var predicates = signals.ToPredicates(
            osdPlaneExpected: checks.All(check => check.Classification == HealthClassification.ExpectedCold));

        if (predicates.WriteReady)
            _repository.SaveVerifiedClean(_clock.UtcNow, observation.Health.Summary);

        return new IntegritySnapshot
        {
            Raw = observation.Health,
            Checks = checks,
            Predicates = predicates,
            DurabilityFailure = signals.DurabilityFailure,
            Capacity = observation.Capacity,
            CapacityUnavailableReason = observation.CapacityUnavailableReason,
            LastVerifiedCleanAt = _repository.GetLastVerifiedCleanAt(),
            LastVerifiedCleanSummary = _repository.GetLastVerifiedCleanSummary(),
            ObservedAt = _clock.UtcNow
        };
    }

    private IntegritySnapshot Unavailable(string summary)
        => new()
        {
            Raw = new CephHealthRaw
            {
                Status = "UNAVAILABLE",
                Summary = summary,
                Checks = []
            },
            Checks = [],
            Predicates = CephSignals.Unavailable.ToPredicates(osdPlaneExpected: false),
            DurabilityFailure = false,
            Capacity = null,
            CapacityUnavailableReason = summary,
            LastVerifiedCleanAt = _repository.GetLastVerifiedCleanAt(),
            LastVerifiedCleanSummary = _repository.GetLastVerifiedCleanSummary(),
            ObservedAt = _clock.UtcNow
        };

    private static IReadOnlyList<ClassifiedHealthCheck> Classify(
        CephObservation observation,
        CephSignals signals,
        StoragePlaneState plane,
        IReadOnlyList<NooutRecordDto> ownedNoout)
    {
        if (observation.HealthChecks.Count == 0)
            return [StatusOnly(observation.Health.Status)];

        return observation.HealthChecks
            .Select(check => new ClassifiedHealthCheck
            {
                Name = check.Name,
                Detail = check.Display,
                Classification = ClassifyOne(check, plane, ownedNoout),
                Durability = IsDurabilityCheck(check.Name)
            })
            .ToArray();
    }

    /// <summary>
    /// A clean confirmation still produces one entry, so the operator never sees an empty
    /// list beside a non-OK rollup. <see cref="CephHealthRaw.Status"/> carries the rollup
    /// itself; classification works from check names, so there is no status-text matching.
    /// </summary>
    private static ClassifiedHealthCheck StatusOnly(string status)
        => new()
        {
            Name = status,
            Detail = status,
            Classification = string.Equals(status, "HEALTH_OK", StringComparison.Ordinal)
                ? HealthClassification.ExpectedCold
                : HealthClassification.Unexpected,
            Durability = false
        };

    private static HealthClassification ClassifyOne(
        CephHealthCheck check,
        StoragePlaneState plane,
        IReadOnlyList<NooutRecordDto> ownedNoout)
    {
        if (IsDurabilityCheck(check.Name))
            return HealthClassification.Unexpected;

        if (FlagChecks.Contains(check.Name, StringComparer.Ordinal))
            return IsControllerOwnedNoout(check, ownedNoout)
                ? HealthClassification.ExpectedCold
                : HealthClassification.Unexpected;

        // These say the data plane is not serving, which is exactly what a cold appliance looks
        // like — and a real problem at any other time.
        if (ColdPhaseChecks.Contains(check.Name, StringComparer.Ordinal))
            return IsColdPhase(plane)
                ? HealthClassification.ExpectedCold
                : HealthClassification.Unexpected;

        // Anything else is judged on severity rather than against a list of names. Ceph adds
        // health checks between releases, and a name this build has not heard of should not hold
        // writes back merely for being unfamiliar: spec §52 is explicit that ordinary HEALTH_WARN
        // checks are not a reason to stop. An unrecognised HEALTH_ERR still fails closed, and the
        // conditions that genuinely matter — durability, flags, availability, full OSDs — are
        // matched by name above or carried as signals.
        return IsError(check.Severity)
            ? HealthClassification.Unexpected
            : HealthClassification.ExpectedCold;
    }

    private static bool IsError(string severity)
        => severity.Contains("ERR", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A flags check is expected only when StoragePlane holds a controller-owned
    /// <c>noout</c> record and <c>noout</c> is the only flag Ceph is reporting. That keeps
    /// the invariant "clear only flags StoragePlane recorded" and means a flag somebody else
    /// set — or any flag other than <c>noout</c> — still reads as unexpected.
    /// <para>
    /// Ceph does not name the flag set structurally in <c>health detail</c>, so the flags are
    /// matched against a closed vocabulary in the message rather than by open substring test.
    /// </para>
    /// </summary>
    private static bool IsControllerOwnedNoout(CephHealthCheck check, IReadOnlyList<NooutRecordDto> ownedNoout)
    {
        if (ownedNoout.Count == 0)
            return false;

        var mentioned = OsdMapFlags
            .Where(flag => check.Message.Contains(flag, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return mentioned.Length > 0
               && mentioned.All(flag => string.Equals(flag, "noout", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsColdPhase(StoragePlaneState plane)
        => plane is StoragePlaneState.Cold
            or StoragePlaneState.Waking
            or StoragePlaneState.Quiescing
            or StoragePlaneState.Sleeping;

    private static bool IsDurabilityCheck(string name)
        => DurabilityChecks.Contains(name, StringComparer.Ordinal);

    private static readonly string[] DurabilityChecks =
    [
        "OBJECT_UNFOUND",
        "PG_DAMAGED",
        "OSD_SCRUB_ERRORS"
    ];

    private static readonly string[] FlagChecks = ["OSDMAP_FLAGS", "OSD_FLAGS"];

    /// <summary>
    /// Expected while the data plane is cold, waking or going to sleep: these are what an
    /// appliance with deliberately stopped OSDs looks like. Outside those phases they mean
    /// something is wrong, so they hold writes back.
    /// </summary>
    private static readonly string[] ColdPhaseChecks =
    [
        "OSD_DOWN",
        "OSD_HOST_DOWN",
        "OSD_ROOT_DOWN",

        // Genuinely says data is unavailable, so outside a cold phase it holds reads back too.
        "PG_AVAILABILITY",
        "SLOW_OPS"
    ];

    private static readonly string[] OsdMapFlags =
    [
        "noout",
        "noup",
        "nodown",
        "noin",
        "nobackfill",
        "norebalance",
        "norecover",
        "nodeep-scrub",
        "noscrub",
        "pauserd",
        "pausewr",
        "full"
    ];
}

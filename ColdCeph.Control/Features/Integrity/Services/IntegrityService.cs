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

    public IReadOnlyDictionary<int, OsdMembershipDto> ListOsdMembership()
    {
        try
        {
            return _ceph.ListOsdMembership();
        }
        catch (Exception)
        {
            return new Dictionary<int, OsdMembershipDto>();
        }
    }

    public IntegritySnapshot GetIntegrity()
    {
        try
        {
            return ReadIntegrity();
        }
        catch (Exception exception)
        {
            var snapshot = Unavailable(exception.Message);
            lock (_snapshotGate)
                _last = snapshot;
            return snapshot;
        }
    }

    private IntegritySnapshot ReadIntegrity()
    {
        var raw = _ceph.GetHealthDetail();
        ClusterCapacityDto? capacity = null;
        string? capacityUnavailableReason = null;
        try
        {
            capacity = _ceph.GetCapacity();
        }
        catch (Exception exception)
        {
            capacityUnavailableReason = exception.Message;
        }
        var plane = _plane.GetState().State;
        var checks = Classify(raw, plane);
        var predicates = BuildPredicates(checks);
        if (predicates.WriteReady)
            _repository.SaveVerifiedClean(_clock.UtcNow, raw.Summary);

        var snapshot = new IntegritySnapshot
        {
            Raw = raw,
            Checks = checks,
            Predicates = predicates,
            Capacity = capacity,
            CapacityUnavailableReason = capacityUnavailableReason,
            LastVerifiedCleanAt = _repository.GetLastVerifiedCleanAt(),
            LastVerifiedCleanSummary = _repository.GetLastVerifiedCleanSummary(),
            ObservedAt = _clock.UtcNow
        };
        lock (_snapshotGate)
            _last = snapshot;
        return snapshot;
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
            Capacity = null,
            CapacityUnavailableReason = summary,
            Predicates = new ReadinessPredicates
            {
                ControlPlaneAvailable = false,
                OsdPlaneExpected = false,
                ReadReady = false,
                WriteReady = false,
                SleepSafe = false
            },
            LastVerifiedCleanAt = _repository.GetLastVerifiedCleanAt(),
            LastVerifiedCleanSummary = _repository.GetLastVerifiedCleanSummary(),
            ObservedAt = _clock.UtcNow
        };

    private static IReadOnlyList<ClassifiedHealthCheck> Classify(CephHealthRaw raw, StoragePlaneState plane)
    {
        var checks = raw.Checks.Count == 0 ? [raw.Summary] : raw.Checks;
        return checks.Select(check => new ClassifiedHealthCheck
        {
            Name = check.Split(':')[0],
            Detail = check,
            Classification = ClassifyOne(check, plane)
        }).ToArray();
    }

    private static HealthClassification ClassifyOne(string check, StoragePlaneState plane)
    {
        if (Contains(check, "unfound") || Contains(check, "inconsistent") || Contains(check, "incomplete"))
            return HealthClassification.Unexpected;

        if (Contains(check, "noout"))
            return HealthClassification.ExpectedCold;

        var expectedCold = Contains(check, "osd down")
                           || Contains(check, "osds down")
                           || Contains(check, "pg inactive")
                           || Contains(check, "stale")
                           || Contains(check, "HEALTH_ERR")
                           || Contains(check, "HEALTH_WARN");

        if (expectedCold && plane is StoragePlaneState.Cold or StoragePlaneState.Sleeping or StoragePlaneState.Quiescing or StoragePlaneState.Waking)
            return HealthClassification.ExpectedCold;

        if (rawHealthy(check))
            return HealthClassification.ExpectedCold;

        return string.IsNullOrWhiteSpace(check) || Contains(check, "HEALTH_OK")
            ? HealthClassification.ExpectedCold
            : HealthClassification.Unexpected;
    }

    private static bool rawHealthy(string check)
        => Contains(check, "HEALTH_OK") || string.Equals(check, "HEALTH_OK", StringComparison.OrdinalIgnoreCase);

    private ReadinessPredicates BuildPredicates(IReadOnlyList<ClassifiedHealthCheck> checks)
    {
        var unexpected = checks.Any(check => check.Classification == HealthClassification.Unexpected);
        var quorum = _ceph.GetQuorumAvailable();
        var unfound = _ceph.GetHasUnfound();
        var inconsistent = _ceph.GetHasInconsistent();
        var stale = _ceph.GetHasStaleOrIncomplete();
        var recovery = _ceph.GetHasRecoveryOrBackfill();
        var full = _ceph.GetHasFullOsds();
        var active = _ceph.GetPgsActive();
        var clean = _ceph.GetPgsClean();
        var control = quorum && !unexpected;
        var readReady = control && active && !stale && !unfound;
        var writeReady = readReady && clean && !inconsistent && !recovery && !full;
        var sleepSafe = control && clean && !unfound && !inconsistent && !recovery && !full;
        return new ReadinessPredicates
        {
            ControlPlaneAvailable = quorum,
            OsdPlaneExpected = !unexpected,
            ReadReady = readReady,
            WriteReady = writeReady,
            SleepSafe = sleepSafe
        };
    }

    private static bool Contains(string text, string token)
        => text.Contains(token, StringComparison.OrdinalIgnoreCase);
}

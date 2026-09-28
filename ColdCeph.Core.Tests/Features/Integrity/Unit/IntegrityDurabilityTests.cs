using ColdCeph.Core.Features.Integrity.DTOs;

namespace ColdCeph.Core.Tests.Features.Integrity.Unit;

[TestFixture]
public sealed class IntegrityDurabilityTests
{
    [Test]
    public void Unfound_objects_are_a_durability_failure()
    {
        Assert.That(IntegrityDurability.IsFailure("OBJECT_UNFOUND: unfound objects"), Is.True);
        Assert.That(IntegrityDurability.HasFailure(Snapshot("OBJECT_UNFOUND", "unfound objects")), Is.True);
    }

    [Test]
    public void Too_few_pgs_is_not_a_durability_failure()
    {
        Assert.That(IntegrityDurability.IsFailure("TOO_FEW_PGS: too few PGs"), Is.False);
        Assert.That(IntegrityDurability.HasFailure(Snapshot("TOO_FEW_PGS", "too few PGs")), Is.False);
    }

    [Test]
    public void Osd_down_is_not_a_durability_failure()
    {
        Assert.That(IntegrityDurability.IsFailure("OSD_DOWN: 1 osds down"), Is.False);
        Assert.That(IntegrityDurability.HasFailure(Snapshot("OSD_DOWN", "1 osds down")), Is.False);
    }

    [Test]
    public void Empty_health_ok_is_not_a_durability_failure()
    {
        Assert.That(IntegrityDurability.HasFailure(Snapshot(name: "", detail: "")), Is.False);
        Assert.That(IntegrityDurability.HasFailure(new IntegritySnapshot
        {
            Raw = new CephHealthRaw { Status = "HEALTH_OK", Summary = "HEALTH_OK", Checks = [] },
            Checks = [],
            Predicates = ReadyPredicates(),
            Capacity = null,
            CapacityUnavailableReason = null,
            LastVerifiedCleanAt = null,
            LastVerifiedCleanSummary = null,
            ObservedAt = DateTimeOffset.UnixEpoch
        }), Is.False);
    }

    private static IntegritySnapshot Snapshot(string name, string detail)
        => new()
        {
            Raw = new CephHealthRaw { Status = "HEALTH_WARN", Summary = detail, Checks = [detail] },
            Checks =
            [
                new ClassifiedHealthCheck
                {
                    Name = name,
                    Detail = $"{name}: {detail}",
                    Classification = HealthClassification.Unexpected
                }
            ],
            Predicates = ReadyPredicates(),
            Capacity = null,
            CapacityUnavailableReason = null,
            LastVerifiedCleanAt = null,
            LastVerifiedCleanSummary = null,
            ObservedAt = DateTimeOffset.UnixEpoch
        };

    private static ReadinessPredicates ReadyPredicates()
        => new()
        {
            ControlPlaneAvailable = true,
            OsdPlaneExpected = true,
            ReadReady = true,
            WriteReady = true,
            SleepSafe = true
        };
}

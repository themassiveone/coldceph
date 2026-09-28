using ColdCeph.Core.Features.Integrity.DTOs;
using ColdCeph.Core.Tests.Support;

namespace ColdCeph.Core.Tests.Features.Integrity.Unit;

/// <summary>
/// The FAULTED verdict is carried on the snapshot, decided once by <see cref="CephSignals"/>.
/// Callers must read that, not re-derive it from check names or message text: the names Ceph
/// uses for damage (<c>PG_DAMAGED</c>, <c>OSD_SCRUB_ERRORS</c>) do not contain the words a text
/// search would look for.
/// </summary>
[TestFixture]
public sealed class IntegrityDurabilityTests
{
    [Test]
    public void A_snapshot_carrying_a_durability_failure_reports_one()
    {
        var snapshot = CephObservations.Snapshot(durabilityFailure: true, "OBJECT_UNFOUND");

        Assert.That(IntegrityDurability.HasFailure(snapshot), Is.True);
    }

    [Test]
    public void A_snapshot_without_one_does_not_report_one()
    {
        var snapshot = CephObservations.Snapshot(durabilityFailure: false, "OSD_DOWN", "TOO_FEW_PGS");

        Assert.That(IntegrityDurability.HasFailure(snapshot), Is.False);
    }

    [Test]
    public void A_clean_snapshot_with_no_checks_does_not_report_one()
    {
        var snapshot = CephObservations.Snapshot(durabilityFailure: false);

        Assert.That(snapshot.Checks, Is.Empty);
        Assert.That(IntegrityDurability.HasFailure(snapshot), Is.False);
    }

    /// <summary>
    /// Damage reported under a name that says nothing about inconsistency still faults, which
    /// is the case a name-substring test silently missed.
    /// </summary>
    [Test]
    public void Damage_named_PG_DAMAGED_reports_a_failure()
    {
        var snapshot = CephObservations.Snapshot(durabilityFailure: true, "PG_DAMAGED");

        Assert.That(snapshot.Checks.Single().Name, Does.Not.Contain("inconsistent").IgnoreCase);
        Assert.That(IntegrityDurability.HasFailure(snapshot), Is.True);
    }

    /// <summary>
    /// And a name that happens to contain one of those words does not fault on its own — the
    /// flag on the snapshot decides.
    /// </summary>
    [Test]
    public void A_check_name_containing_a_loaded_word_does_not_fault_by_itself()
    {
        var snapshot = CephObservations.Snapshot(durabilityFailure: false, "RGW_INCOMPLETE_MULTIPART_UPLOADS");

        Assert.That(snapshot.Checks.Single().Name, Does.Contain("INCOMPLETE"));
        Assert.That(IntegrityDurability.HasFailure(snapshot), Is.False);
    }
}

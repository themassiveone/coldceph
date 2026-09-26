using System.Text.Json;
using ColdCeph.Core.Features.StoragePlane.DTOs;

namespace ColdCeph.Core.Tests.Features.StoragePlane.Unit;

[TestFixture]
public sealed class StoragePlaneSnapshotSerializationTests
{
    [Test]
    public void Snapshot_round_trips_state_and_trust_flag()
    {
        var snapshot = new StoragePlaneSnapshot
        {
            State = StoragePlaneState.Cold,
            LeaseHolder = null,
            ActiveOperationId = null,
            JournalStep = "idle",
            ObservedAt = DateTimeOffset.Parse("2026-01-02T03:04:05Z"),
            Trusted = false
        };

        var json = JsonSerializer.Serialize(snapshot);
        var restored = JsonSerializer.Deserialize<StoragePlaneSnapshot>(json);

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.State, Is.EqualTo(StoragePlaneState.Cold));
        Assert.That(restored.Trusted, Is.False);
    }

    [Test]
    public void Snapshot_does_not_treat_missing_state_as_cold()
    {
        const string json = """{"leaseHolder":null,"activeOperationId":null,"journalStep":"idle","observedAt":"2026-01-02T03:04:05+00:00","trusted":true}""";

        Assert.That(() => JsonSerializer.Deserialize<StoragePlaneSnapshot>(json), Throws.Exception);
    }
}

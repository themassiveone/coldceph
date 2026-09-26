using System.Text.Json;
using ColdCeph.Core.Features.Integrity.DTOs;

namespace ColdCeph.Core.Tests.Features.Integrity.Unit;

[TestFixture]
public sealed class IntegritySnapshotSerializationTests
{
    [Test]
    public void Predicates_round_trip_write_ready_separately_from_read_ready()
    {
        var predicates = new ReadinessPredicates
        {
            ControlPlaneAvailable = true,
            OsdPlaneExpected = true,
            ReadReady = true,
            WriteReady = false,
            SleepSafe = false
        };

        var restored = JsonSerializer.Deserialize<ReadinessPredicates>(JsonSerializer.Serialize(predicates));

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.ReadReady, Is.True);
        Assert.That(restored.WriteReady, Is.False);
    }

    [Test]
    public void Predicates_do_not_default_write_ready_when_omitted()
    {
        const string json = """{"controlPlaneAvailable":true,"osdPlaneExpected":true,"readReady":true,"sleepSafe":true}""";

        Assert.That(() => JsonSerializer.Deserialize<ReadinessPredicates>(json), Throws.Exception);
    }
}

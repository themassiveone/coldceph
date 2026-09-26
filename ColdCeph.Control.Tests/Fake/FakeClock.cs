using ColdCeph.Control.Shared;

namespace ColdCeph.Control.Tests.Fake;

public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
}

using ColdCeph.Control.Features.Integrity.Interfaces;

namespace ColdCeph.Control.Tests.Fake;

public sealed class MemoryIntegrityRepository : IIntegrityRepository
{
    public DateTimeOffset? LastAt { get; private set; }
    public string? LastSummary { get; private set; }

    public DateTimeOffset? GetLastVerifiedCleanAt() => LastAt;
    public string? GetLastVerifiedCleanSummary() => LastSummary;

    public void SaveVerifiedClean(DateTimeOffset at, string summary)
    {
        LastAt = at;
        LastSummary = summary;
    }
}

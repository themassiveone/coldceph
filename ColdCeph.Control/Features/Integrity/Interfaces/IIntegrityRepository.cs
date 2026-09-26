namespace ColdCeph.Control.Features.Integrity.Interfaces;

public interface IIntegrityRepository
{
    DateTimeOffset? GetLastVerifiedCleanAt();
    string? GetLastVerifiedCleanSummary();
    void SaveVerifiedClean(DateTimeOffset at, string summary);
}

using ColdCeph.Control.Features.Integrity.Interfaces;
using ColdCeph.Core.Features.Integrity.DTOs;

namespace ColdCeph.Control.Tests.Fake;

public sealed class FakeCephQueryProvider : ICephQueryProvider
{
    public CephHealthRaw Health { get; set; } = new()
    {
        Status = "HEALTH_OK",
        Summary = "HEALTH_OK",
        Checks = []
    };

    public bool QuorumAvailable { get; set; } = true;
    public bool PgsActive { get; set; } = true;
    public bool PgsClean { get; set; } = true;
    public bool HasUnfound { get; set; }
    public bool HasInconsistent { get; set; }
    public bool HasRecoveryOrBackfill { get; set; }
    public bool HasStaleOrIncomplete { get; set; }
    public bool HasFullOsds { get; set; }
    public IReadOnlyList<string> HealthChecks { get; set; } = [];

    public bool ThrowOnHealth { get; set; }

    public int HealthDetailCalls { get; set; }

    public CephHealthRaw GetHealthDetail()
    {
        if (ThrowOnHealth)
            throw new InvalidOperationException("ceph unavailable");
        HealthDetailCalls++;
        return Health;
    }
    public bool GetQuorumAvailable() => QuorumAvailable;
    public bool GetPgsActive() => PgsActive;
    public bool GetPgsClean() => PgsClean;
    public bool GetHasUnfound() => HasUnfound;
    public bool GetHasInconsistent() => HasInconsistent;
    public bool GetHasRecoveryOrBackfill() => HasRecoveryOrBackfill;
    public bool GetHasStaleOrIncomplete() => HasStaleOrIncomplete;
    public bool GetHasFullOsds() => HasFullOsds;
    public IReadOnlyList<string> GetHealthChecks() => HealthChecks.Count > 0 ? HealthChecks : Health.Checks;
}

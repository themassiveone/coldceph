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
    public bool ThrowOnCapacity { get; set; }

    public ClusterCapacityDto Capacity { get; set; } = new()
    {
        TotalBytes = 3_000_000_000,
        UsedBytes = 1_000_000_000,
        AvailableBytes = 2_000_000_000
    };

    public int HealthDetailCalls { get; set; }
    public int CapacityCalls { get; set; }

    public CephHealthRaw GetHealthDetail()
    {
        if (ThrowOnHealth)
            throw new InvalidOperationException("ceph unavailable");
        HealthDetailCalls++;
        return Health;
    }
    public ClusterCapacityDto GetCapacity()
    {
        if (ThrowOnCapacity)
            throw new InvalidOperationException("capacity unavailable");
        CapacityCalls++;
        return Capacity;
    }
    public bool GetQuorumAvailable() => QuorumAvailable;
    public bool GetPgsActive() => PgsActive;
    public bool GetPgsClean() => PgsClean;
    public bool GetHasUnfound() => HasUnfound;
    public bool GetHasInconsistent() => HasInconsistent;
    public bool GetHasRecoveryOrBackfill() => HasRecoveryOrBackfill;
    public bool GetHasStaleOrIncomplete() => HasStaleOrIncomplete;
    public bool GetHasFullOsds() => HasFullOsds;
    public IReadOnlyDictionary<int, OsdMembershipDto> OsdMembership { get; set; } =
        new Dictionary<int, OsdMembershipDto>();

    public IReadOnlyList<string> GetHealthChecks() => HealthChecks.Count > 0 ? HealthChecks : Health.Checks;

    public bool ThrowOnMembership { get; set; }

    public int MembershipCalls { get; set; }

    public IReadOnlyDictionary<int, OsdMembershipDto> ListOsdMembership()
    {
        if (ThrowOnMembership)
            throw new InvalidOperationException("ceph unavailable");
        MembershipCalls++;
        return OsdMembership;
    }
}

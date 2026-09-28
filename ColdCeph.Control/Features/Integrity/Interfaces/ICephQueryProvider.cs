using ColdCeph.Core.Features.Integrity.DTOs;

namespace ColdCeph.Control.Features.Integrity.Interfaces;

public interface ICephQueryProvider
{
    CephHealthRaw GetHealthDetail();
    ClusterCapacityDto GetCapacity();
    bool GetQuorumAvailable();
    bool GetPgsActive();
    bool GetPgsClean();
    bool GetHasUnfound();
    bool GetHasInconsistent();
    bool GetHasRecoveryOrBackfill();
    bool GetHasStaleOrIncomplete();
    bool GetHasFullOsds();
    IReadOnlyList<string> GetHealthChecks();
    IReadOnlyDictionary<int, OsdMembershipDto> ListOsdMembership();
}

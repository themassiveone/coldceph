using ColdCeph.Core.Features.Integrity.DTOs;

namespace ColdCeph.Control.Features.Integrity.Interfaces;

public interface ICephQueryProvider
{
    /// <summary>
    /// One confirmation. Reads health, capacity, quorum and PG state in a single pass so
    /// that "the monitor is queried once per external request" holds structurally, with no
    /// cache and no clock involved.
    /// </summary>
    CephObservation GetObservation();

    /// <summary>
    /// The Ceph <c>osd dump</c> up/in overlay. A separate confirmation because it serves a
    /// separate external request: one Osds list GET.
    /// </summary>
    IReadOnlyDictionary<int, OsdMembershipDto> ListOsdMembership();
}

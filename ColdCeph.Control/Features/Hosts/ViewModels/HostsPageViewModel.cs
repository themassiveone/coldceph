using ColdCeph.Core.Features.Hosts.DTOs;

namespace ColdCeph.Control.Features.Hosts.ViewModels;

public sealed class HostsPageViewModel
{
    public required IReadOnlyList<HostJoinRequestDto> Pending { get; init; }
    public required IReadOnlyList<HostDto> Enrolled { get; init; }
    public required IReadOnlyList<HostJoinRequestDto> Blocked { get; init; }
}

using ColdCeph.Core.Features.Hosts.DTOs;

namespace ColdCeph.Control.Features.Hosts.Models;

public sealed class HostsRecord
{
    public Dictionary<string, HostDto> Hosts { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, HostJoinRequestDto> Pending { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, HostJoinRequestDto> Blocked { get; init; } = new(StringComparer.Ordinal);
}

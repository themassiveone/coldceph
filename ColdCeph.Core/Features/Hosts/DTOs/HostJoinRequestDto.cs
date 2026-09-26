namespace ColdCeph.Core.Features.Hosts.DTOs;

public sealed record HostJoinRequestDto
{
    public required string HostId { get; init; }
    public required string Hostname { get; init; }
    public required Uri Endpoint { get; init; }
    public required DateTimeOffset RequestedAt { get; init; }
}

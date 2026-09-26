namespace ColdCeph.Core.Features.Hosts.DTOs;

public sealed record HostDto
{
    public required string HostId { get; init; }
    public required string Hostname { get; init; }
    public required Uri Endpoint { get; init; }
    public required DateTimeOffset LastHeartbeat { get; init; }
    public required bool Alive { get; init; }
}

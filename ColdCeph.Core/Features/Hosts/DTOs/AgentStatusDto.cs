namespace ColdCeph.Core.Features.Hosts.DTOs;

public sealed record AgentStatusDto
{
    public required string HostId { get; init; }
    public required string Hostname { get; init; }
    public required DateTimeOffset ObservedAt { get; init; }
}

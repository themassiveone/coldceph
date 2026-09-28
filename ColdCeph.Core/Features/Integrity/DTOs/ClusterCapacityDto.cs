namespace ColdCeph.Core.Features.Integrity.DTOs;

public sealed record ClusterCapacityDto
{
    public required long TotalBytes { get; init; }
    public required long UsedBytes { get; init; }
    public required long AvailableBytes { get; init; }
}

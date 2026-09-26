namespace ColdCeph.Core.Features.Integrity.DTOs;

public sealed record CephHealthRaw
{
    public required string Status { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<string> Checks { get; init; }
}

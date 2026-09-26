namespace ColdCeph.Core.Features.StoragePlane.DTOs;

public sealed record StoragePlaneSnapshot
{
    public required StoragePlaneState State { get; init; }
    public required string? LeaseHolder { get; init; }
    public required string? ActiveOperationId { get; init; }
    public required string? JournalStep { get; init; }
    public required DateTimeOffset ObservedAt { get; init; }
    public required bool Trusted { get; init; }
}

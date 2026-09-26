namespace ColdCeph.Core.Features.StoragePlane.DTOs;

public sealed record TransitionLeaseDto
{
    public required string LeaseId { get; init; }
    public required string Holder { get; init; }
    public required string OperationId { get; init; }
    public required DateTimeOffset AcquiredAt { get; init; }
    public required DateTimeOffset Deadline { get; init; }
}

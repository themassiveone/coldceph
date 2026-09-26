namespace ColdCeph.Core.Features.S3.DTOs;

public sealed record S3PendingWorkDto
{
    public required bool HasPendingWork { get; init; }
    public required int ActiveCount { get; init; }
    public required int QueuedCount { get; init; }
    public required DateTimeOffset? LastActivity { get; init; }
}

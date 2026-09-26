namespace ColdCeph.Core.Features.Operations.DTOs;

public sealed record OperationRecordDto
{
    public required string OperationId { get; init; }
    public required string Kind { get; init; }
    public required string Initiator { get; init; }
    public required string Summary { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset? CompletedAt { get; init; }
}

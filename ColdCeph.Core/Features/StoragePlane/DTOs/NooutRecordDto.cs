namespace ColdCeph.Core.Features.StoragePlane.DTOs;

public sealed record NooutRecordDto
{
    public required string Scope { get; init; }
    public required string OperationId { get; init; }
    public required string ControllerInstance { get; init; }
    public required DateTimeOffset SetAt { get; init; }
    public required bool PreviousState { get; init; }
}

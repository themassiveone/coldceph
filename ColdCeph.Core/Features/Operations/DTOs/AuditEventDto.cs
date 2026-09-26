namespace ColdCeph.Core.Features.Operations.DTOs;

public sealed record AuditEventDto
{
    public required string EventId { get; init; }
    public required string OperationId { get; init; }
    public required string Kind { get; init; }
    public required string Detail { get; init; }
    public required DateTimeOffset At { get; init; }
}

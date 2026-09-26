namespace ColdCeph.Core.Features.Osds.DTOs;

public sealed record OsdMutationRequest
{
    public required int OsdId { get; init; }
    public required string OperationId { get; init; }
    public required string ControllerIdentity { get; init; }
    public required DateTimeOffset Deadline { get; init; }
    public required bool DesiredRunning { get; init; }
}

namespace ColdCeph.Core.Features.Osds.DTOs;

public sealed record OsdDto
{
    public required int OsdId { get; init; }
    public required string HostId { get; init; }
    public required string? DeviceId { get; init; }
    public required bool Up { get; init; }
    public required bool In { get; init; }
    public required bool ProcessRunning { get; init; }
}

namespace ColdCeph.Core.Features.Devices.DTOs;

public sealed record DeviceDto
{
    public required string DeviceId { get; init; }
    public required string HostId { get; init; }
    public required int? MappedOsdId { get; init; }
    public required string? Wwn { get; init; }
    public required string? Serial { get; init; }
    public required string? Path { get; init; }
    public required DevicePowerState PowerState { get; init; }
}

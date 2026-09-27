namespace ColdCeph.Core.Features.Devices.DTOs;

public sealed record HostDevicesObservationDto
{
    public required string HostId { get; init; }
    public required IReadOnlyList<DeviceDto> Devices { get; init; }
    public string? Error { get; init; }
}

using ColdCeph.Core.Features.Devices.DTOs;

namespace ColdCeph.Control.Features.Devices.ViewModels;

public sealed record DevicesPageViewModel
{
    public required IReadOnlyList<DeviceDto> Devices { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
}

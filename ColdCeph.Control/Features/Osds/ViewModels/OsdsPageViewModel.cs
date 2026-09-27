using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Control.Features.Osds.ViewModels;

public sealed record OsdsPageViewModel
{
    public required IReadOnlyList<OsdDto> Osds { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
}

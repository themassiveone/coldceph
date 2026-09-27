namespace ColdCeph.Core.Features.Osds.DTOs;

public sealed record HostOsdsObservationDto
{
    public required string HostId { get; init; }
    public required IReadOnlyList<OsdDto> Osds { get; init; }
    public string? Error { get; init; }
}

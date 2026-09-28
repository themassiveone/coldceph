namespace ColdCeph.Core.Features.Integrity.DTOs;

/// <summary>
/// One entry from <c>ceph health detail --format json</c>'s <c>checks</c> object.
/// <see cref="Name"/> is the check key, which Ceph treats as a stable identifier
/// (<c>OSD_DOWN</c>, <c>OBJECT_UNFOUND</c>, …). Classification keys on the name and on
/// PG states, never on <see cref="Message"/>, which is prose and changes between releases.
/// </summary>
public sealed record CephHealthCheck
{
    public required string Name { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }

    public string Display => string.IsNullOrWhiteSpace(Message) ? Name : $"{Name}: {Message}";
}

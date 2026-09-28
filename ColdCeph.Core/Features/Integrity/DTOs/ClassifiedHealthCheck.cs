namespace ColdCeph.Core.Features.Integrity.DTOs;

public sealed record ClassifiedHealthCheck
{
    public required string Name { get; init; }
    public required string Detail { get; init; }
    public required HealthClassification Classification { get; init; }

    /// <summary>
    /// True when this entry is one of the durability failures in spec §52 (unfound,
    /// inconsistent, incomplete). Set from <see cref="CephSignals"/> rather than by
    /// matching the message text.
    /// </summary>
    public required bool Durability { get; init; }
}

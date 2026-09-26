namespace ColdCeph.Core.Features.Integrity.DTOs;

public sealed record ClassifiedHealthCheck
{
    public required string Name { get; init; }
    public required string Detail { get; init; }
    public required HealthClassification Classification { get; init; }
}

namespace ColdCeph.Core.Features.StoragePlane.DTOs;

public sealed record StoragePlaneReadinessDto(
    bool ForwardReads,
    bool ForwardWrites);

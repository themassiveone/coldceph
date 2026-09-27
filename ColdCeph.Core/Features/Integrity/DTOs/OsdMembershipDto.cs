namespace ColdCeph.Core.Features.Integrity.DTOs;

public sealed record OsdMembershipDto
{
    public required int OsdId { get; init; }
    public required bool Up { get; init; }
    public required bool In { get; init; }
}

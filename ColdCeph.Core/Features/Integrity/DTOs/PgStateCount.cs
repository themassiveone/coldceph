namespace ColdCeph.Core.Features.Integrity.DTOs;

/// <summary>
/// One entry from a Ceph <c>pgs_by_state</c> array. <see cref="StateName"/> is a
/// <c>+</c>-joined set of PG state tokens, for example <c>active+clean</c> or
/// <c>active+recovering+degraded</c>.
/// </summary>
public sealed record PgStateCount
{
    public required string StateName { get; init; }
    public required int Count { get; init; }

    public IEnumerable<string> Tokens
        => StateName.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    public bool HasToken(string token)
        => Tokens.Any(candidate => string.Equals(candidate, token, StringComparison.OrdinalIgnoreCase));
}

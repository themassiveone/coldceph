namespace ColdCeph.Core.Features.Integrity.DTOs;

/// <summary>
/// Whether a confirmation found one of spec §52's durability failures.
/// The verdict is computed once, by <see cref="CephSignals"/>, from Ceph's health check
/// names and PG state tokens, and carried on the snapshot. Callers read it; they do not
/// re-derive it from message text.
/// </summary>
public static class IntegrityDurability
{
    public static bool HasFailure(IntegritySnapshot snapshot) => snapshot.DurabilityFailure;
}

using Xcepto.Config;

namespace ColdCeph.E2E.Tests.Support;

/// <summary>
/// Xcepto budgets: the second value is the per-test timeout.
/// <para>
/// These are sized for what a journey actually does. Every Ceph confirmation is four
/// <c>docker exec … ceph</c> invocations against the container, and S3 admission takes one after
/// reaching READY, so even a single GET through the cold endpoint costs several seconds. The old
/// fifteen-second per-test budget predates any journey that confirmed Ceph at all.
/// </para>
/// <para>
/// Still fail-fast in the sense that matters: bounded, and in seconds rather than minutes of
/// hanging.
/// </para>
/// </summary>
public static class SharedTimeout
{
    public static TimeoutConfig Timeout => new(TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(60));

    /// <summary>
    /// For journeys that wait on Ceph doing real work — a write reaching an OSD, a sleep settling,
    /// a flags check appearing on the next mgr tick.
    /// </summary>
    public static TimeoutConfig LongTimeout => new(TimeSpan.FromMinutes(8), TimeSpan.FromMinutes(4));
}

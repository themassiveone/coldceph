using Xcepto.Config;

namespace ColdCeph.E2E.Tests.Support;

public static class SharedTimeout
{
    public static TimeoutConfig Timeout => new(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(15));

    /// <summary>
    /// For journeys that wait on Ceph doing real work — a write reaching an OSD, a wake settling.
    /// Still fail-fast in the sense that matters: seconds, and bounded.
    /// </summary>
    public static TimeoutConfig LongTimeout => new(TimeSpan.FromSeconds(180), TimeSpan.FromSeconds(30));
}

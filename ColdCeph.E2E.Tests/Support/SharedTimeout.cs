using Xcepto.Config;

namespace ColdCeph.E2E.Tests.Support;

public static class SharedTimeout
{
    public static TimeoutConfig Timeout => new(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(15));
}

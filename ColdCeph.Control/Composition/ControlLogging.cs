using Microsoft.Extensions.Logging;

namespace ColdCeph.Control.Composition;

public static class ControlLogging
{
    public static bool ShouldLog(string? category, LogLevel level)
    {
        if (level < LogLevel.Information)
            return false;
        if (category is not null
            && category.StartsWith("System.Net.Http.HttpClient", StringComparison.Ordinal)
            && level < LogLevel.Warning)
        {
            return false;
        }

        return true;
    }
}

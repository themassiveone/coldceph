using Microsoft.Extensions.Logging;

namespace ColdCeph.Agent.Composition;

public static class AgentLogging
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

using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Integrity.Interfaces;
using ColdCeph.Control.Shared;
using ColdCeph.Core.Features.Integrity.DTOs;
using System.Text.Json;

namespace ColdCeph.Control.Features.Integrity.Providers;

public sealed class CephCliQueryProvider : ICephQueryProvider
{
    private readonly ControlConfig _config;
    private readonly IProcessRunner _runner;
    private readonly IClock _clock;
    private readonly object _gate = new();
    private readonly Dictionary<string, CachedOutput> _cache = new(StringComparer.Ordinal);

    public CephCliQueryProvider(ControlConfig config, IProcessRunner runner)
        : this(config, runner, new SystemClock())
    {
    }

    public CephCliQueryProvider(ControlConfig config, IProcessRunner runner, IClock clock)
    {
        _config = config;
        _runner = runner;
        _clock = clock;
    }

    public CephHealthRaw GetHealthDetail()
    {
        var json = Run("health", "detail");
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var status = document.RootElement.TryGetProperty("status", out var statusNode)
            ? statusNode.GetString() ?? "HEALTH_ERR"
            : "HEALTH_ERR";
        var checks = ParseChecks(document.RootElement);
        return new CephHealthRaw
        {
            Status = status,
            Summary = string.Join("; ", checks),
            Checks = checks
        };
    }

    public bool GetQuorumAvailable()
    {
        var json = Run("quorum_status");
        return json.Contains("quorum", StringComparison.OrdinalIgnoreCase);
    }

    public bool GetPgsActive() => !GetHasStaleOrIncomplete();

    public bool GetPgsClean()
    {
        var json = Run("pg", "stat");
        return json.Contains("active+clean", StringComparison.OrdinalIgnoreCase) && !GetHasRecoveryOrBackfill();
    }

    public bool GetHasUnfound() => ContainsHealth("unfound");

    public bool GetHasInconsistent() => ContainsHealth("inconsistent");

    public bool GetHasRecoveryOrBackfill()
        => ContainsHealth("recover") || ContainsHealth("backfill");

    public bool GetHasStaleOrIncomplete()
        => ContainsHealth("stale") || ContainsHealth("incomplete");

    public bool GetHasFullOsds()
        => ContainsHealth("full") || ContainsHealth("nearfull");

    public IReadOnlyList<string> GetHealthChecks()
    {
        var json = Run("health", "detail");
        if (string.IsNullOrWhiteSpace(json))
            return [];
        using var document = JsonDocument.Parse(json);
        return ParseChecks(document.RootElement);
    }

    private bool ContainsHealth(string token)
        => GetHealthChecks().Any(check => check.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> ParseChecks(JsonElement root)
    {
        if (!root.TryGetProperty("checks", out var checks) || checks.ValueKind != JsonValueKind.Object)
            return [];

        return checks.EnumerateObject().Select(check =>
        {
            var message = check.Value.ValueKind == JsonValueKind.Object
                          && check.Value.TryGetProperty("summary", out var summary)
                          && summary.ValueKind == JsonValueKind.Object
                          && summary.TryGetProperty("message", out var msg)
                ? msg.GetString() ?? check.Value.ToString()
                : check.Value.ToString();
            return $"{check.Name}: {message}";
        }).ToArray();
    }

    private string Run(params string[] command)
    {
        var key = string.Join('\u001f', command);
        lock (_gate)
        {
            var now = _clock.UtcNow;
            if (_config.CephQueryCacheTtl > TimeSpan.Zero
                && _cache.TryGetValue(key, out var hit)
                && hit.ExpiresAt > now)
                return hit.Output;

            var output = _runner.Run(_config.CephBinary, _config.BuildCephArguments(["--format", "json", ..command]));
            if (_config.CephQueryCacheTtl > TimeSpan.Zero)
                _cache[key] = new CachedOutput(output, now + _config.CephQueryCacheTtl);
            return output;
        }
    }

    private sealed record CachedOutput(string Output, DateTimeOffset ExpiresAt);
}

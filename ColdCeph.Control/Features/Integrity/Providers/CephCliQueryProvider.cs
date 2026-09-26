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

    public CephCliQueryProvider(ControlConfig config, IProcessRunner runner)
    {
        _config = config;
        _runner = runner;
    }

    public CephHealthRaw GetHealthDetail()
    {
        var json = _runner.Run(_config.CephBinary, ["--format", "json", "health", "detail"]);
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var status = document.RootElement.TryGetProperty("status", out var statusNode)
            ? statusNode.GetString() ?? "HEALTH_ERR"
            : "HEALTH_ERR";
        var checks = GetHealthChecks();
        return new CephHealthRaw
        {
            Status = status,
            Summary = string.Join("; ", checks),
            Checks = checks
        };
    }

    public bool GetQuorumAvailable()
    {
        var json = _runner.Run(_config.CephBinary, ["--format", "json", "quorum_status"]);
        return json.Contains("quorum", StringComparison.OrdinalIgnoreCase);
    }

    public bool GetPgsActive() => !GetHasStaleOrIncomplete();

    public bool GetPgsClean()
    {
        var json = _runner.Run(_config.CephBinary, ["--format", "json", "pg", "stat"]);
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
        var json = _runner.Run(_config.CephBinary, ["--format", "json", "health", "detail"]);
        if (string.IsNullOrWhiteSpace(json))
            return [];
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("checks", out var checks) || checks.ValueKind != JsonValueKind.Object)
            return [];

        return checks.EnumerateObject().Select(check => $"{check.Name}: {check.Value}").ToArray();
    }

    private bool ContainsHealth(string token)
        => GetHealthChecks().Any(check => check.Contains(token, StringComparison.OrdinalIgnoreCase))
           || GetHealthDetail().Summary.Contains(token, StringComparison.OrdinalIgnoreCase);
}

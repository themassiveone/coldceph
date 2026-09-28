using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Integrity.Interfaces;
using ColdCeph.Control.Shared;
using ColdCeph.Core.Features.Integrity.DTOs;
using System.Text.Json;

namespace ColdCeph.Control.Features.Integrity.Providers;

/// <summary>
/// Reads Ceph through the <c>ceph</c> CLI.
/// <para>
/// A confirmation is exactly four invocations — <c>health detail</c>, <c>status</c>,
/// <c>quorum_status</c>, <c>pg stat</c> — issued once by <see cref="GetObservation"/>.
/// There is no output cache: the single-confirmation property comes from the shape of the
/// call, not from a TTL that expires mid-confirmation on a slow cluster.
/// </para>
/// <para>
/// Every value is read from a named JSON field. Nothing here greps Ceph's prose.
/// </para>
/// </summary>
public sealed class CephCliQueryProvider : ICephQueryProvider
{
    private readonly ControlConfig _config;
    private readonly IProcessRunner _runner;

    public CephCliQueryProvider(ControlConfig config, IProcessRunner runner)
    {
        _config = config;
        _runner = runner;
    }

    public CephObservation GetObservation()
    {
        var (health, checks) = ReadHealth();
        var quorum = ReadQuorumAvailable();
        var pgStates = ReadPgStates();

        ClusterCapacityDto? capacity = null;
        string? capacityUnavailableReason = null;
        try
        {
            capacity = ReadCapacity();
        }
        catch (Exception exception)
        {
            capacityUnavailableReason = exception.Message;
        }

        return new CephObservation
        {
            Health = health,
            HealthChecks = checks,
            QuorumAvailable = quorum,
            PgStates = pgStates,
            Capacity = capacity,
            CapacityUnavailableReason = capacityUnavailableReason
        };
    }

    public IReadOnlyDictionary<int, OsdMembershipDto> ListOsdMembership()
    {
        using var document = Parse(Run("osd", "dump"));
        if (!document.RootElement.TryGetProperty("osds", out var osds) || osds.ValueKind != JsonValueKind.Array)
            return new Dictionary<int, OsdMembershipDto>();

        var membership = new Dictionary<int, OsdMembershipDto>();
        foreach (var osd in osds.EnumerateArray())
        {
            if (!osd.TryGetProperty("osd", out var idNode) || !idNode.TryGetInt32(out var osdId))
                continue;
            membership[osdId] = new OsdMembershipDto
            {
                OsdId = osdId,
                Up = ReadFlag(osd, "up"),
                In = ReadFlag(osd, "in")
            };
        }

        return membership;
    }

    private (CephHealthRaw Health, IReadOnlyList<CephHealthCheck> Checks) ReadHealth()
    {
        using var document = Parse(Run("health", "detail"));
        var root = document.RootElement;
        var status = root.TryGetProperty("status", out var statusNode)
            ? statusNode.GetString() ?? "HEALTH_ERR"
            : "HEALTH_ERR";
        var checks = ParseChecks(root);
        return (
            new CephHealthRaw
            {
                Status = status,
                Summary = checks.Count == 0 ? status : string.Join("; ", checks.Select(check => check.Display)),
                Checks = checks.Select(check => check.Display).ToArray()
            },
            checks);
    }

    /// <summary>
    /// Quorum means the monitor named a quorum, which <c>quorum_status</c> reports in
    /// <c>quorum_names</c>. The JSON always contains the string "quorum" — in key names —
    /// so a substring test over the document is true even when quorum has been lost.
    /// </summary>
    private bool ReadQuorumAvailable()
    {
        using var document = Parse(Run("quorum_status"));
        var root = document.RootElement;
        if (root.TryGetProperty("quorum_names", out var names) && names.ValueKind == JsonValueKind.Array)
            return names.EnumerateArray().Any(name => !string.IsNullOrWhiteSpace(name.GetString()));
        if (root.TryGetProperty("quorum", out var ranks) && ranks.ValueKind == JsonValueKind.Array)
            return ranks.GetArrayLength() > 0;
        return false;
    }

    private IReadOnlyList<PgStateCount> ReadPgStates()
    {
        using var document = Parse(Run("pg", "stat"));
        return ParsePgStates(document.RootElement);
    }

    private ClusterCapacityDto ReadCapacity()
    {
        using var document = Parse(Run("status"));
        if (!document.RootElement.TryGetProperty("pgmap", out var pgmap))
            throw new InvalidOperationException("Ceph status did not include capacity information.");

        return new ClusterCapacityDto
        {
            TotalBytes = ReadNonNegativeInt64(pgmap, "bytes_total"),
            UsedBytes = ReadNonNegativeInt64(pgmap, "bytes_used"),
            AvailableBytes = ReadNonNegativeInt64(pgmap, "bytes_avail")
        };
    }

    internal static IReadOnlyList<PgStateCount> ParsePgStates(JsonElement root)
    {
        if (!root.TryGetProperty("pgs_by_state", out var states) || states.ValueKind != JsonValueKind.Array)
            return [];

        var parsed = new List<PgStateCount>();
        foreach (var state in states.EnumerateArray())
        {
            if (!state.TryGetProperty("state_name", out var nameNode))
                continue;
            var name = nameNode.GetString();
            if (string.IsNullOrWhiteSpace(name))
                continue;
            var count = state.TryGetProperty("count", out var countNode) && countNode.TryGetInt32(out var parsedCount)
                ? parsedCount
                : 0;
            parsed.Add(new PgStateCount { StateName = name, Count = count });
        }

        return parsed;
    }

    internal static IReadOnlyList<CephHealthCheck> ParseChecks(JsonElement root)
    {
        if (!root.TryGetProperty("checks", out var checks) || checks.ValueKind != JsonValueKind.Object)
            return [];

        return checks.EnumerateObject()
            .Select(check => new CephHealthCheck
            {
                Name = check.Name,
                Severity = check.Value.ValueKind == JsonValueKind.Object
                           && check.Value.TryGetProperty("severity", out var severity)
                    ? severity.GetString() ?? string.Empty
                    : string.Empty,
                Message = ReadCheckMessage(check.Value)
            })
            .ToArray();
    }

    private static string ReadCheckMessage(JsonElement check)
    {
        if (check.ValueKind != JsonValueKind.Object)
            return check.ToString();
        if (check.TryGetProperty("summary", out var summary)
            && summary.ValueKind == JsonValueKind.Object
            && summary.TryGetProperty("message", out var message))
            return message.GetString() ?? string.Empty;
        return string.Empty;
    }

    /// <summary>
    /// Ceph writes OSD <c>up</c>/<c>in</c> as 0/1 numbers in <c>osd dump</c>, but as
    /// booleans elsewhere. Accept both, and treat anything else as not up / not in.
    /// </summary>
    private static bool ReadFlag(JsonElement osd, string name)
    {
        if (!osd.TryGetProperty(name, out var value))
            return false;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt32(out var number) && number != 0,
            JsonValueKind.True => true,
            _ => false
        };
    }

    private static long ReadNonNegativeInt64(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value)
            || !value.TryGetInt64(out var parsed)
            || parsed < 0)
            throw new InvalidOperationException($"Ceph status returned invalid {name} capacity.");
        return parsed;
    }

    private static JsonDocument Parse(string json)
        => JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);

    private string Run(params string[] command)
    {
        var invoke = _config.InvokeCeph(["--format", "json", ..command]);
        return _runner.Run(invoke.FileName, invoke.Arguments);
    }
}

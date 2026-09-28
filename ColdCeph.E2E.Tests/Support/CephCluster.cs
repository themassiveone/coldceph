using System.Text.Json;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace ColdCeph.E2E.Tests.Support;

/// <summary>
/// A single-container Ceph for the Xcepto journeys.
/// <para>
/// This deliberately does <em>not</em> force the cluster to <c>HEALTH_OK</c>. It used to mute the
/// demo cluster's warnings and then refuse to start unless health was clean, with the reason
/// stated in the throw: "Integrity would FAULT at READY". Classification is the thing most worth
/// testing, and that arrangement guaranteed it only ever saw the empty case. The journeys now
/// assert what ColdCeph concludes from the warnings a real small cluster actually reports.
/// </para>
/// <para>
/// What it does wait for is the cluster being usable at all: a monitor quorum, an OSD that is up
/// and in, and RGW answering. Those are preconditions, not verdicts about health.
/// </para>
/// </summary>
public sealed class CephCluster : IAsyncDisposable
{
    public const string Image = "quay.io/ceph/daemon:v7.0.3-stable-7.0-quincy-centos-stream8";
    public const int RgwPort = 8080;

    private readonly IContainer _container;
    private bool _disposed;

    public Uri RgwAddress { get; private set; } = new("http://127.0.0.1");
    public string ContainerId => _container.Id;
    public string DemoBucket => "cold";

    /// <summary>
    /// The CRUSH bucket Control scopes its <c>noout</c> to: the host bucket the demo OSD is
    /// actually under, discovered from <c>osd find</c>.
    /// <para>
    /// The point is that <c>osd set-group noout</c> covers a real OSD, so Ceph raises the flags
    /// check ColdCeph has to classify. Creating a fresh empty bucket meant the flag covered nothing
    /// and the check never appeared; creating one and moving the host into it as a new CRUSH
    /// <em>root</em> took the OSD out of <c>root=default</c>, which broke pool mapping and stopped
    /// RGW from ever finishing its bootstrap. Using the bucket that is already there changes no
    /// topology at all.
    /// </para>
    /// </summary>
    public string NooutScope { get; private set; } = "hdd-osds";

    public CephCluster()
    {
        _container = new ContainerBuilder(Image)
            .WithHostname("ceph")
            .WithCommand("demo")
            .WithPrivileged(true)
            .WithCreateParameterModifier(parameters =>
            {
                parameters.HostConfig ??= new HostConfig();
                parameters.HostConfig.ShmSize = 1L * 1024 * 1024 * 1024;
            })
            .WithEnvironment(new Dictionary<string, string>
            {
                ["NETWORK_AUTO_DETECT"] = "4",
                ["DEMO_DAEMONS"] = "mon,mgr,osd,rgw",
                ["CEPH_DEMO_UID"] = "coldceph",
                ["CEPH_DEMO_ACCESS_KEY"] = "coldceph",
                ["CEPH_DEMO_SECRET_KEY"] = "coldcephsecret",
                ["CEPH_DEMO_BUCKET"] = "cold",
                ["RGW_CIVETWEB_PORT"] = "8080",
                ["RGW_NAME"] = "localhost",
                ["CEPH_DASHBOARD"] = "0"
            })
            .WithPortBinding(RgwPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilCommandIsCompleted("ceph -s", options => options.WithTimeout(TimeSpan.FromMinutes(6))))
            .Build();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _container.StartAsync(cancellationToken);
        RgwAddress = new Uri($"http://127.0.0.1:{_container.GetMappedPublicPort(RgwPort)}");
        await WaitForQuorumAsync(cancellationToken);
        await WaitForOsdUpAsync(cancellationToken);
        // RGW is still creating its pools at this point, so nothing touches the cluster's shape
        // until it has finished. Interfering here is what stopped it coming up.
        await WaitForRgwAsync(cancellationToken);
        NooutScope = await ReadOsdHostBucketAsync();
        await SizePoolsForOneOsdAsync();
        await WaitForCleanPgsAsync(cancellationToken);
    }

    /// <summary>
    /// Sets every pool to one replica, because this cluster has one OSD.
    /// <para>
    /// Spec §27.4 defines <c>write_ready</c> as read-ready AND PGs <c>active+clean</c> AND zero
    /// degraded objects. A single-OSD cluster whose pools ask for three replicas sits permanently at
    /// <c>active+undersized+degraded</c>: active, so reads work, but never clean — so ColdCeph
    /// correctly refuses writes forever. That is the product working as specified, not a bug in it,
    /// and the fix is to give the test a validly configured cluster rather than to relax a
    /// durability gate to get a journey green.
    /// </para>
    /// <para>
    /// This is not the same as the muting this harness used to do. Nothing here changes what
    /// ColdCeph is told or what it concludes: the cluster is made genuinely clean, and the
    /// classifier and predicates still see whatever Ceph actually reports.
    /// </para>
    /// </summary>
    private async Task SizePoolsForOneOsdAsync()
    {
        var pools = (await CephAsync("osd", "pool", "ls"))
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        foreach (var pool in pools)
        {
            // min_size first: lowering size below min_size is rejected.
            _ = await TryCephAsync("osd", "pool", "set", pool, "min_size", "1");
            _ = await TryCephAsync("osd", "pool", "set", pool, "size", "1", "--yes-i-really-mean-it");
        }
    }

    /// <summary>
    /// Waits for every PG to reach <c>active+clean</c>, which is what spec §27.4 requires before
    /// ColdCeph will admit a write. Resizing pools moves data, so this has to settle before a
    /// journey asserts anything about write admission.
    /// </summary>
    private async Task WaitForCleanPgsAsync(CancellationToken cancellationToken)
        => await WaitFor(
            "every PG active+clean",
            async () =>
            {
                // From `status`, not `pg stat`: `pg stat --format json` does not carry
                // pgs_by_state, which is the same trap that made the provider read no PG states
                // at all and hold every readiness predicate closed on a healthy cluster.
                var json = await TryCephAsync("status", "--format", "json");
                return string.IsNullOrWhiteSpace(json) ? false : AllPgsClean(json);
            },
            TimeSpan.FromMinutes(3),
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await _container.DisposeAsync();
    }

    /// <summary>Runs a ceph command in the container, failing loudly rather than silently.</summary>
    public async Task<string> CephAsync(params string[] command)
    {
        var result = await _container.ExecAsync(["ceph", ..command]);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"ceph {string.Join(' ', command)} exited {result.ExitCode}: {result.Stderr}");
        return result.Stdout;
    }

    public async Task<string> TryCephAsync(params string[] command)
    {
        var result = await _container.ExecAsync(["ceph", ..command]);
        return result.ExitCode == 0 ? result.Stdout : string.Empty;
    }

    private async Task WaitForQuorumAsync(CancellationToken cancellationToken)
        => await WaitFor(
            "a monitor quorum",
            async () => (await TryCephAsync("quorum_status", "--format", "json")).Contains("quorum_names", StringComparison.Ordinal),
            TimeSpan.FromMinutes(2),
            cancellationToken);

    /// <summary>
    /// An OSD that is up and in is a precondition for anything ColdCeph does. Unlike HEALTH_OK it
    /// says nothing about whether the cluster is warning about something.
    /// </summary>
    private async Task WaitForOsdUpAsync(CancellationToken cancellationToken)
        => await WaitFor(
            "an OSD that is up and in",
            async () =>
            {
                var json = await TryCephAsync("osd", "dump", "--format", "json");
                return json.Contains("\"up\":1", StringComparison.Ordinal)
                       || json.Contains("\"up\": 1", StringComparison.Ordinal);
            },
            TimeSpan.FromMinutes(3),
            cancellationToken);

    /// <summary>
    /// Reads the CRUSH host bucket osd.0 sits under, so the noout scope names something real
    /// without changing the cluster's topology.
    /// </summary>
    /// <summary>
    /// Whether every <c>pgmap.pgs_by_state</c> group carries both <c>active</c> and <c>clean</c>.
    /// Parsed here rather than through Control's provider, which keeps its parser internal.
    /// </summary>
    private static bool AllPgsClean(string statusJson)
    {
        using var document = JsonDocument.Parse(statusJson);
        if (!document.RootElement.TryGetProperty("pgmap", out var pgmap)
            || !pgmap.TryGetProperty("pgs_by_state", out var states)
            || states.ValueKind != JsonValueKind.Array
            || states.GetArrayLength() == 0)
            return false;

        foreach (var state in states.EnumerateArray())
        {
            var name = state.TryGetProperty("state_name", out var node) ? node.GetString() : null;
            if (name is null)
                return false;
            var tokens = name.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (!tokens.Contains("active", StringComparer.OrdinalIgnoreCase)
                || !tokens.Contains("clean", StringComparer.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private async Task<string> ReadOsdHostBucketAsync()
    {
        var json = await CephAsync("osd", "find", "0", "--format", "json");
        var match = System.Text.RegularExpressions.Regex.Match(json, "\"host\"\\s*:\\s*\"(?<host>[^\"]+)\"");
        if (!match.Success)
            throw new InvalidOperationException(
                $"Could not read osd.0's CRUSH host from `ceph osd find 0`, so the noout scope would "
                + $"name a bucket that does not exist and sleep would fail. Output: {json}");
        return match.Groups["host"].Value;
    }

    /// <summary>
    /// RGW is the last daemon the demo entrypoint brings up and the slowest, because it creates its
    /// own pools first. The budget is generous for that reason: it used to be masked by a two-minute
    /// HEALTH_OK wait ahead of it, and removing that wait left it too tight on a cold CI runner.
    /// </summary>
    private async Task WaitForRgwAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        await WaitFor(
            $"RGW to answer at {RgwAddress}",
            async () =>
            {
                try
                {
                    var response = await client.GetAsync(RgwAddress, cancellationToken);
                    return (int)response.StatusCode < 500;
                }
                catch (Exception)
                {
                    return false;
                }
            },
            TimeSpan.FromMinutes(5),
            cancellationToken);
    }

    /// <summary>
    /// What the cluster looked like when a wait gave up. Gathered here rather than from a CI step
    /// after the fact, because Testcontainers disposes the container on teardown and a later
    /// <c>docker logs</c> finds nothing to read.
    /// </summary>
    private async Task<string> DiagnosticsAsync()
    {
        var parts = new List<string>();
        try
        {
            var logs = await _container.GetLogsAsync();
            var combined = logs.Stdout + "\n" + logs.Stderr;
            var lines = combined.Split('\n');
            parts.Add("--- container log (last 12 lines) ---");
            parts.AddRange(lines.Skip(Math.Max(0, lines.Length - 12)));
        }
        catch (Exception exception)
        {
            parts.Add($"--- container log unavailable: {exception.Message}");
        }

        foreach (var command in new[] { "-s", "osd tree", "health detail" })
        {
            var output = await TryCephAsync(command.Split(' '));
            parts.Add($"--- ceph {command} ---");
            parts.Add(string.IsNullOrWhiteSpace(output) ? "(no output)" : output.Trim());
        }

        return string.Join('\n', parts);
    }

    private async Task WaitFor(
        string what,
        Func<Task<bool>> ready,
        TimeSpan limit,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await ready())
                return;
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        throw new InvalidOperationException(
            $"Demo Ceph did not reach {what} within {limit.TotalMinutes:0.#} minutes."
            + Environment.NewLine
            + await DiagnosticsAsync());
    }
}

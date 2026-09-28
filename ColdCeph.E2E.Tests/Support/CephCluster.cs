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

    /// <summary>
    /// The CRUSH bucket StoragePlane scopes its <c>noout</c> to. The OSD is moved into it during
    /// setup, so <c>osd set-group noout</c> actually covers something and Ceph raises the health
    /// check ColdCeph has to classify. Pointing it at an empty bucket meant the flag affected
    /// nothing and the check never appeared.
    /// </summary>
    public const string NooutScope = "hdd-osds";

    private readonly IContainer _container;
    private bool _disposed;

    public Uri RgwAddress { get; private set; } = new("http://127.0.0.1");
    public string ContainerId => _container.Id;
    public string DemoBucket => "cold";

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
        await MoveOsdIntoNooutScopeAsync();
        await WaitForRgwAsync(cancellationToken);
    }

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
    /// Puts the OSD's host under the bucket StoragePlane scopes noout to, so a scoped noout is a
    /// real flag on a real OSD and Ceph reports it.
    /// </summary>
    private async Task MoveOsdIntoNooutScopeAsync()
    {
        // add-bucket is idempotent in effect but errors if it already exists, so it is tolerated.
        _ = await TryCephAsync("osd", "crush", "add-bucket", NooutScope, "root");
        await CephAsync("osd", "crush", "move", "ceph", $"root={NooutScope}");
    }

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
            TimeSpan.FromMinutes(2),
            cancellationToken);
    }

    private static async Task WaitFor(
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

        throw new InvalidOperationException($"Demo Ceph did not reach {what} within {limit.TotalMinutes:0.#} minutes.");
    }
}

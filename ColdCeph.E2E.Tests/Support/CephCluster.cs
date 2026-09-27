using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace ColdCeph.E2E.Tests.Support;

public sealed class CephCluster : IAsyncDisposable
{
    public const string Image = "quay.io/ceph/daemon:v7.0.3-stable-7.0-quincy-centos-stream8";
    public const int RgwPort = 8080;
    public const string NooutScope = "hdd-osds";

    private readonly IContainer _container;
    private bool _disposed;

    public Uri RgwAddress { get; private set; } = new("http://127.0.0.1");
    public string ContainerId => _container.Id;

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
        await SilenceDemoHealthWarningsAsync();
        await EnsureNooutScopeAsync();
        await WaitForHealthOkAsync();
        await WaitForRgwAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await _container.DisposeAsync();
    }

    private async Task SilenceDemoHealthWarningsAsync()
    {
        string[][] commands =
        [
            ["ceph", "config", "set", "global", "mon_warn_on_pool_no_redundancy", "false"],
            ["ceph", "config", "set", "global", "mon_warn_on_too_few_osds", "false"],
            ["ceph", "health", "mute", "POOL_NO_REDUNDANCY"],
            ["ceph", "health", "mute", "TOO_FEW_OSDS"],
            ["ceph", "health", "mute", "AUTH_INSECURE_GLOBAL_ID_RECLAIM"],
            ["ceph", "health", "mute", "AUTH_INSECURE_GLOBAL_ID_RECLAIM_ALLOWED"]
        ];
        foreach (var command in commands)
        {
            try
            {
                _ = await _container.ExecAsync(command);
            }
            catch (Exception)
            {
            }
        }
    }

    private async Task WaitForHealthOkAsync()
    {
        var deadline = DateTime.UtcNow.AddMinutes(2);
        string? last = null;
        while (DateTime.UtcNow < deadline)
        {
            var result = await _container.ExecAsync(["ceph", "health", "--format", "json"]);
            last = result.Stdout;
            if (result.ExitCode == 0 && last.Contains("HEALTH_OK", StringComparison.Ordinal))
                return;
            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        throw new InvalidOperationException(
            $"Demo Ceph did not reach HEALTH_OK (Integrity would FAULT at READY). Last health: {last}");
    }

    private async Task EnsureNooutScopeAsync()
    {
        try
        {
            _ = await _container.ExecAsync(["ceph", "osd", "crush", "add-bucket", NooutScope, "host"]);
        }
        catch (Exception)
        {
        }

        try
        {
            _ = await _container.ExecAsync(["ceph", "osd", "crush", "move", NooutScope, "root=default"]);
        }
        catch (Exception)
        {
        }
    }

    private async Task WaitForRgwAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddMinutes(2);
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var response = await client.GetAsync(RgwAddress, cancellationToken);
                if ((int)response.StatusCode < 500)
                    return;
            }
            catch (Exception ex)
            {
                last = ex;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        throw new InvalidOperationException($"RGW did not become reachable at {RgwAddress}.", last);
    }
}

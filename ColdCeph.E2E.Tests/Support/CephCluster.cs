using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace ColdCeph.E2E.Tests.Support;

public sealed class CephCluster : IAsyncDisposable
{
    public const string Image = "quay.io/ceph/daemon:v7.0.3-stable-7.0-quincy-centos-stream8";
    public const int RgwPort = 8080;

    private readonly IContainer _container;
    private string? _wrapperPath;
    private bool _disposed;

    public Uri RgwAddress { get; private set; } = new("http://127.0.0.1");
    public string CephBinary { get; private set; } = "ceph";

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
        CephBinary = WriteWrapper(_container.Id);
        await SilenceDemoHealthWarningsAsync();
        await WaitForRgwAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await _container.DisposeAsync();
        if (_wrapperPath is not null)
        {
            try
            {
                File.Delete(_wrapperPath);
            }
            catch (IOException)
            {
            }
        }
    }

    private async Task SilenceDemoHealthWarningsAsync()
    {
        string[][] commands =
        [
            ["ceph", "config", "set", "global", "mon_warn_on_pool_no_redundancy", "false"],
            ["ceph", "config", "set", "global", "mon_warn_on_too_few_osds", "false"]
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

    private string WriteWrapper(string containerId)
    {
        var suffix = containerId.Length >= 12 ? containerId[..12] : containerId;
        var path = Path.Join(Path.GetTempPath(), $"coldceph-ceph-{suffix}");
        File.WriteAllText(path, $"""
            #!/usr/bin/env bash
            exec docker exec {containerId} ceph "$@"
            """);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        _wrapperPath = path;
        return path;
    }
}

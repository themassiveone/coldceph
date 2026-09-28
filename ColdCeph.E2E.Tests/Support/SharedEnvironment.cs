using System.Text.RegularExpressions;
using ColdCeph.Node.Composition;
using ColdCeph.Node.Features.Devices.Interfaces;
using ColdCeph.Node.Features.Devices.Services;
using ColdCeph.Node.Features.Osds.Interfaces;
using ColdCeph.Node.Features.Osds.Services;
using ColdCeph.Control.Composition;
using ColdCeph.Core.Features.Devices.DTOs;
using ColdCeph.Core.Features.Osds.DTOs;
using ColdCeph.Core.Features.S3.DTOs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ColdCeph.E2E.Tests.Support;

public sealed class SharedEnvironment
{
    public const string OperatorPassword = "changeme";
    public const string NodeToken = "changeme";
    public const string HostId = "dev";
    public const string DemoBucket = "cold";
    public const int OperatorPort = 18080;
    public const int S3Port = 17480;
    public const int NodePort = 17080;

    private static WebApplication? _control;
    private static WebApplication? _node;

    public static Uri ControlAddress { get; } = new($"http://127.0.0.1:{OperatorPort}");
    public static Uri S3Address { get; } = new($"http://127.0.0.1:{S3Port}");
    public static Uri NodeAddress { get; } = new($"http://127.0.0.1:{NodePort}");
    public static Uri RgwAddress { get; private set; } = new("http://127.0.0.1");
    public static string RepositoryRoot { get; private set; } = "";
    public static string CephContainer { get; private set; } = "";
    public static string NooutScope { get; private set; } = "hdd-osds";

    public static async Task StartAsync(CephCluster ceph)
    {
        RepositoryRoot = FindRepositoryRoot();
        CephContainer = ceph.ContainerId;
        RgwAddress = ceph.RgwAddress;
        NooutScope = ceph.NooutScope;
        _node = await StartNodeAsync();
        _control = await StartControlAsync();
        await WaitUntilListeningAsync(NodeAddress, "/health");
        await WaitUntilListeningAsync(ControlAddress, "/health");
    }

    public static async Task StopAsync()
    {
        if (_control is not null)
            await _control.DisposeAsync();
        if (_node is not null)
            await _node.DisposeAsync();
        _control = null;
        _node = null;
    }

    private static async Task<WebApplication> StartNodeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        var config = new NodeConfig
        {
            Port = NodePort,
            NodeToken = NodeToken,
            HostId = HostId,
            Hostname = HostId,
            ControlEndpoint = ControlAddress,
            AdvertiseEndpoint = NodeAddress
        };
        NodeAppComposition.Configure(builder, config);
        builder.Services.RemoveAll<IOsdRuntime>();
        builder.Services.AddSingleton<IOsdRuntime, InMemoryOsdRuntime>();
        builder.Services.RemoveAll<IDiskPower>();
        builder.Services.AddSingleton<IDiskPower, InMemoryDiskPower>();
        builder.WebHost.UseKestrel().UseUrls($"http://127.0.0.1:{NodePort}");
        var app = builder.Build();
        await NodeAppComposition.Initialize(app);
        var osds = app.Services.GetRequiredService<OsdsService>();
        osds.Seed(new OsdDto
        {
            OsdId = 0,
            HostId = HostId,
            DeviceId = "d0",
            Up = false,
            In = true,
            ProcessRunning = false
        });
        if (app.Services.GetRequiredService<IOsdRuntime>() is InMemoryOsdRuntime runtime)
            runtime.Know(0);
        app.Services.GetRequiredService<DevicesService>().Seed(new DeviceDto
        {
            DeviceId = "d0",
            HostId = HostId,
            MappedOsdId = 0,
            Wwn = "wwn-e2e",
            Serial = "serial-e2e",
            Path = "/dev/e2e",
            PowerState = DevicePowerState.Standby
        });
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> StartControlAsync()
    {
        var data = Path.Join(Path.GetTempPath(), "coldceph-e2e", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
            ContentRootPath = Path.Join(RepositoryRoot, "ColdCeph.Control")
        });
        var config = new ControlConfig
        {
            OperatorPort = OperatorPort,
            S3Port = S3Port,
            RgwEndpoint = RgwAddress,
            DataDirectory = data,
            OperatorPassword = OperatorPassword,
            NodeToken = NodeToken,
            CephBinary = "ceph",
            CephContainer = CephContainer,
            ConfiguredNodeEndpoints = [NodeAddress],
            ConfiguredNodeHostId = HostId,
            S3Mode = S3AdmissionMode.Retry,
            NooutScope = NooutScope,
            IdleTimeout = TimeSpan.FromHours(1),
            BindHttpListeners = true
        };
        AppComposition.Configure(builder, config);
        builder.WebHost.UseKestrel().UseUrls($"http://127.0.0.1:{OperatorPort}", $"http://127.0.0.1:{S3Port}");
        var app = builder.Build();
        await AppComposition.Initialize(app);
        await app.StartAsync();
        return app;
    }

    private static async Task WaitUntilListeningAsync(Uri address, string path)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(15);
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await client.GetAsync(new Uri(address, path));
                if ((int)response.StatusCode < 500)
                    return;
            }
            catch (Exception ex)
            {
                last = ex;
            }

            await Task.Delay(200);
        }

        throw new InvalidOperationException($"Service at {address} did not listen.", last);
    }

    public static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Join(directory.FullName, "coldceph.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }

    public static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        if (!match.Success)
            match = Regex.Match(html, "value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"");
        if (!match.Success)
            throw new InvalidOperationException("Antiforgery token not found.");
        return match.Groups[1].Value;
    }
}

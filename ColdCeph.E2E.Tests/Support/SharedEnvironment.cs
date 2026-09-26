using System.Text.RegularExpressions;
using ColdCeph.Agent.Composition;
using ColdCeph.Agent.Features.Devices.Interfaces;
using ColdCeph.Agent.Features.Devices.Services;
using ColdCeph.Agent.Features.Osds.Interfaces;
using ColdCeph.Agent.Features.Osds.Services;
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
    public const string AgentToken = "changeme";
    public const string HostId = "dev";
    public const int OperatorPort = 18080;
    public const int S3Port = 17480;
    public const int AgentPort = 17080;

    private static WebApplication? _control;
    private static WebApplication? _agent;

    public static Uri ControlAddress { get; } = new($"http://127.0.0.1:{OperatorPort}");
    public static Uri S3Address { get; } = new($"http://127.0.0.1:{S3Port}");
    public static Uri AgentAddress { get; } = new($"http://127.0.0.1:{AgentPort}");
    public static Uri RgwAddress { get; private set; } = new("http://127.0.0.1");
    public static string RepositoryRoot { get; private set; } = "";
    public static string CephBinary { get; private set; } = "ceph";

    public static async Task StartAsync(CephCluster ceph)
    {
        RepositoryRoot = FindRepositoryRoot();
        CephBinary = ceph.CephBinary;
        RgwAddress = ceph.RgwAddress;
        _agent = await StartAgentAsync();
        _control = await StartControlAsync();
        await WaitUntilListeningAsync(AgentAddress, "/health");
        await WaitUntilListeningAsync(ControlAddress, "/health");
    }

    public static async Task StopAsync()
    {
        if (_control is not null)
            await _control.DisposeAsync();
        if (_agent is not null)
            await _agent.DisposeAsync();
        _control = null;
        _agent = null;
    }

    private static async Task<WebApplication> StartAgentAsync()
    {
        var builder = WebApplication.CreateBuilder();
        var config = new AgentConfig
        {
            Port = AgentPort,
            AgentToken = AgentToken,
            HostId = HostId,
            Hostname = HostId
        };
        AgentAppComposition.Configure(builder, config);
        builder.Services.RemoveAll<IOsdRuntime>();
        builder.Services.AddSingleton<IOsdRuntime, InMemoryOsdRuntime>();
        builder.Services.RemoveAll<IDiskPower>();
        builder.Services.AddSingleton<IDiskPower, InMemoryDiskPower>();
        builder.WebHost.UseKestrel().UseUrls($"http://127.0.0.1:{AgentPort}");
        var app = builder.Build();
        await AgentAppComposition.Initialize(app);
        app.Services.GetRequiredService<OsdsService>().Seed(new OsdDto
        {
            OsdId = 0,
            HostId = HostId,
            DeviceId = "d0",
            Up = false,
            In = true,
            ProcessRunning = false
        });
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
            AgentToken = AgentToken,
            CephBinary = CephBinary,
            ConfiguredAgentEndpoints = [AgentAddress],
            ConfiguredAgentHostId = HostId,
            S3Mode = S3AdmissionMode.Retry,
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

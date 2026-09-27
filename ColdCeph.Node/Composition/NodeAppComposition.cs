using ColdCeph.Node.Features.Devices.Controllers;
using ColdCeph.Node.Features.Devices.Interfaces;
using ColdCeph.Node.Features.Devices.Providers;
using ColdCeph.Node.Features.Devices.Services;
using ColdCeph.Node.Features.Hosts.Controllers;
using ColdCeph.Node.Features.Hosts.Interfaces;
using ColdCeph.Node.Features.Hosts.Providers;
using ColdCeph.Node.Features.Hosts.Services;
using ColdCeph.Node.Features.Osds.Controllers;
using ColdCeph.Node.Features.Osds.Interfaces;
using ColdCeph.Node.Features.Osds.Providers;
using ColdCeph.Node.Features.Osds.Services;
using ColdCeph.Node.Shared;

namespace ColdCeph.Node.Composition;

public static class NodeAppComposition
{
    public static void Configure(WebApplicationBuilder builder, NodeConfig config)
    {
        builder.Services.AddSingleton(config);
        builder.Logging.AddFilter(NodeLogging.ShouldLog);
        builder.Services.AddHttpClient("control");
        builder.Services.AddSingleton<IProcessRunner, SystemProcessRunner>();
        builder.Services.AddSingleton<HostsService>();
        builder.Services.AddSingleton<HostsController>();
        builder.Services.AddSingleton<IControlHeartbeatClient, HttpControlHeartbeatClient>();
        builder.Services.AddHostedService<HostsHeartbeatLoop>();
        if (config.ControlEndpoint is null)
        {
            builder.Services.AddSingleton<IControlOsdsReporter, NoopControlOsdsReporter>();
            builder.Services.AddSingleton<IControlDevicesReporter, NoopControlDevicesReporter>();
        }
        else
        {
            builder.Services.AddSingleton<IControlOsdsReporter, HttpControlOsdsReporter>();
            builder.Services.AddSingleton<IControlDevicesReporter, HttpControlDevicesReporter>();
        }
        builder.Services.AddSingleton<IOsdRuntime>(services =>
        {
            var node = services.GetRequiredService<NodeConfig>();
            var runner = services.GetRequiredService<IProcessRunner>();
            return string.IsNullOrWhiteSpace(node.OsdContainer)
                ? new SystemdOsdRuntime(runner)
                : new DockerExecOsdRuntime(runner, node.OsdContainer!);
        });
        builder.Services.AddSingleton<OsdsService>();
        builder.Services.AddSingleton<OsdsController>();
        builder.Services.AddHostedService<OsdsReportLoop>();
        builder.Services.AddSingleton<IDiskPower>(services =>
        {
            var node = services.GetRequiredService<NodeConfig>();
            return string.IsNullOrWhiteSpace(node.OsdContainer)
                ? new HdparmDiskPower(services.GetRequiredService<IProcessRunner>())
                : new MemoryDiskPower();
        });
        builder.Services.AddSingleton<DevicesService>();
        builder.Services.AddSingleton<DevicesController>();
        builder.Services.AddHostedService<DevicesReportLoop>();
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(Program).Assembly);
    }

    public static Task Initialize(WebApplication app)
    {
        var config = app.Services.GetRequiredService<NodeConfig>();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/health"))
            {
                await next();
                return;
            }

            if (!context.Request.Headers.TryGetValue("X-ColdCeph-Token", out var token)
                || token != config.NodeToken)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next();
        });
        app.UseRouting();
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.MapControllers();
        return Task.CompletedTask;
    }
}

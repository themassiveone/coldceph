using ColdCeph.Agent.Features.Devices.Controllers;
using ColdCeph.Agent.Features.Devices.Interfaces;
using ColdCeph.Agent.Features.Devices.Providers;
using ColdCeph.Agent.Features.Devices.Services;
using ColdCeph.Agent.Features.Hosts.Controllers;
using ColdCeph.Agent.Features.Hosts.Interfaces;
using ColdCeph.Agent.Features.Hosts.Providers;
using ColdCeph.Agent.Features.Hosts.Services;
using ColdCeph.Agent.Features.Osds.Controllers;
using ColdCeph.Agent.Features.Osds.Interfaces;
using ColdCeph.Agent.Features.Osds.Providers;
using ColdCeph.Agent.Features.Osds.Services;
using ColdCeph.Agent.Shared;

namespace ColdCeph.Agent.Composition;

public static class AgentAppComposition
{
    public static void Configure(WebApplicationBuilder builder, AgentConfig config)
    {
        builder.Services.AddSingleton(config);
        builder.Logging.AddFilter(AgentLogging.ShouldLog);
        builder.Services.AddHttpClient("control");
        builder.Services.AddSingleton<IProcessRunner, SystemProcessRunner>();
        builder.Services.AddSingleton<HostsService>();
        builder.Services.AddSingleton<HostsController>();
        builder.Services.AddSingleton<IControlHeartbeatClient, HttpControlHeartbeatClient>();
        builder.Services.AddHostedService<HostsHeartbeatLoop>();
        builder.Services.AddSingleton<IOsdRuntime, SystemdOsdRuntime>();
        builder.Services.AddSingleton<OsdsService>();
        builder.Services.AddSingleton<OsdsController>();
        builder.Services.AddSingleton<IDiskPower, HdparmDiskPower>();
        builder.Services.AddSingleton<DevicesService>();
        builder.Services.AddSingleton<DevicesController>();
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(Program).Assembly);
    }

    public static Task Initialize(WebApplication app)
    {
        var config = app.Services.GetRequiredService<AgentConfig>();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/health"))
            {
                await next();
                return;
            }

            if (!context.Request.Headers.TryGetValue("X-ColdCeph-Token", out var token)
                || token != config.AgentToken)
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

using ColdCeph.Control.Features.Auth.Controllers;
using ColdCeph.Control.Features.Auth.Services;
using ColdCeph.Control.Features.Devices.Controllers;
using ColdCeph.Control.Features.Devices.Interfaces;
using ColdCeph.Control.Features.Devices.Providers;
using ColdCeph.Control.Features.Devices.Services;
using ColdCeph.Control.Features.Hosts.Controllers;
using ColdCeph.Control.Features.Hosts.Services;
using ColdCeph.Control.Features.Integrity.Controllers;
using ColdCeph.Control.Features.Integrity.Interfaces;
using ColdCeph.Control.Features.Integrity.Providers;
using ColdCeph.Control.Features.Integrity.Repositories;
using ColdCeph.Control.Features.Integrity.Services;
using ColdCeph.Control.Features.Operations.Controllers;
using ColdCeph.Control.Features.Operations.Interfaces;
using ColdCeph.Control.Features.Operations.Repositories;
using ColdCeph.Control.Features.Operations.Services;
using ColdCeph.Control.Features.Osds.Controllers;
using ColdCeph.Control.Features.Osds.Interfaces;
using ColdCeph.Control.Features.Osds.Providers;
using ColdCeph.Control.Features.Osds.Services;
using ColdCeph.Control.Features.S3.Controllers;
using ColdCeph.Control.Features.S3.Interfaces;
using ColdCeph.Control.Features.S3.Providers;
using ColdCeph.Control.Features.S3.Repositories;
using ColdCeph.Control.Features.S3.Services;
using ColdCeph.Control.Features.StoragePlane.Controllers;
using ColdCeph.Control.Features.StoragePlane.Interfaces;
using ColdCeph.Control.Features.StoragePlane.Providers;
using ColdCeph.Control.Features.StoragePlane.Repositories;
using ColdCeph.Control.Features.StoragePlane.Services;
using ColdCeph.Control.Shared;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Razor;

namespace ColdCeph.Control.Composition;

public static class AppComposition
{
    public static void Configure(WebApplicationBuilder builder, ControlConfig config)
    {
        builder.Services.AddSingleton(config);
        builder.Logging.AddFilter(ControlLogging.ShouldLog);
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton<SystemProcessRunner>();
        builder.Services.AddSingleton<IProcessRunner>(services =>
            new SerialProcessRunner(services.GetRequiredService<SystemProcessRunner>()));
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddHttpClient("rgw");
        builder.Services.AddHttpClient("node");

        builder.Services.AddSingleton<IStoragePlaneRepository, SqliteStoragePlaneRepository>();
        builder.Services.AddSingleton<INooutProvider, CephNooutProvider>();
        builder.Services.AddSingleton<StoragePlaneService>();
        builder.Services.AddSingleton<StoragePlaneController>();

        builder.Services.AddSingleton<ICephQueryProvider, CephCliQueryProvider>();
        builder.Services.AddSingleton<IIntegrityRepository, SqliteIntegrityRepository>();
        builder.Services.AddSingleton<IntegrityService>();
        builder.Services.AddSingleton<IntegrityController>();

        builder.Services.AddSingleton<IRequestLedger, MemoryRequestLedger>();
        builder.Services.AddSingleton<IRgwProxy, StreamingRgwProxy>();
        builder.Services.AddSingleton<S3Service>();
        builder.Services.AddSingleton<S3Controller>();
        builder.Services.AddSingleton<S3GatewayController>();

        builder.Services.AddSingleton<HostsService>();
        builder.Services.AddSingleton<HostsController>();

        builder.Services.AddSingleton<INodeOsdsClient, HttpNodeOsdsClient>();
        builder.Services.AddSingleton<OsdsService>();
        builder.Services.AddSingleton<OsdsController>();

        builder.Services.AddSingleton<INodeDevicesClient, HttpNodeDevicesClient>();
        builder.Services.AddSingleton<DevicesService>();
        builder.Services.AddSingleton<DevicesController>();

        builder.Services.AddSingleton<IOperationsRepository, SqliteOperationsRepository>();
        builder.Services.AddSingleton<OperationsService>();
        builder.Services.AddSingleton<OperationsController>();

        builder.Services.AddSingleton<AuthService>();
        builder.Services.AddSingleton<AuthController>();

        if (config.BindHttpListeners)
        {
            builder.Services.AddHostedService<StoragePlaneReconciler>();
            builder.Services.AddHostedService<OsdsReconciler>();
            builder.Services.AddHostedService<DevicesReconciler>();
        }

        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = context =>
                    {
                        if (context.Request.Path.StartsWithSegments("/v1"))
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            return Task.CompletedTask;
                        }

                        context.Response.Redirect("/auth/login");
                        return Task.CompletedTask;
                    },
                    OnRedirectToAccessDenied = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    }
                };
            });
        builder.Services.AddAuthorization();
        builder.Services.AddAntiforgery();
        builder.Services.AddControllersWithViews()
            .AddApplicationPart(typeof(Program).Assembly)
            .AddRazorOptions(options => options.ViewLocationExpanders.Add(new SliceViewLocationExpander()));
    }

    public static Task Initialize(WebApplication app)
    {
        var config = app.Services.GetRequiredService<ControlConfig>();
        app.Use(async (context, next) =>
        {
            if (IsS3(context, config))
            {
                var gateway = context.RequestServices.GetRequiredService<S3GatewayController>();
                await gateway.HandleAsync(context);
                return;
            }

            await next();
        });
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/health", (StoragePlaneController plane) => Results.Ok(new { state = plane.GetState().State.ToString() }))
            .AllowAnonymous();
        app.MapControllers();
        return Task.CompletedTask;
    }

    private static bool IsS3(HttpContext context, ControlConfig config)
        => context.Connection.LocalPort == config.S3Port
           || context.Request.Headers.ContainsKey("X-ColdCeph-S3");
}

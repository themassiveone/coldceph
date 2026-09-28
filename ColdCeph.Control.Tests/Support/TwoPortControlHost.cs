using System.Net.Sockets;
using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Integrity.Interfaces;
using ColdCeph.Control.Features.S3.Interfaces;
using ColdCeph.Control.Features.StoragePlane.Interfaces;
using ColdCeph.Control.Features.StoragePlane.Services;
using ColdCeph.Control.Shared;
using ColdCeph.Control.Tests.Fake;
using ColdCeph.Core.Features.Operations.DTOs;
using ColdCeph.Core.Features.S3.DTOs;
using ColdCeph.Core.Features.StoragePlane.DTOs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ColdCeph.Control.Tests.Support;

/// <summary>
/// Control on two real listeners: the operator port and the S3 port.
/// <para>
/// <c>WebApplicationFactory</c> binds one port, which is why an "X-ColdCeph-S3" header used to
/// exist in production to let the tests reach the gateway — a header that in production let any
/// request to the operator port bypass MVC, cookie auth and antiforgery. Dispatch is by port and
/// nothing else, so a test that means to exercise it has to bind both.
/// </para>
/// </summary>
public sealed class TwoPortControlHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    public FakeCephQueryProvider Ceph { get; } = new();
    public FakeRgwObjectStore Rgw { get; } = new();
    public FakeRgwProxy Proxy { get; } = new();
    public int OperatorPort { get; }
    public int S3Port { get; }
    public string DataDirectory { get; }

    public Uri OperatorAddress => new($"http://127.0.0.1:{OperatorPort}");
    public Uri S3Address => new($"http://127.0.0.1:{S3Port}");

    private TwoPortControlHost(S3AdmissionMode mode)
    {
        OperatorPort = FreePort();
        S3Port = FreePort();
        DataDirectory = Path.Join(Path.GetTempPath(), "coldceph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DataDirectory);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
            ContentRootPath = FindControlRoot()
        });
        var config = new ControlConfig
        {
            OperatorPort = OperatorPort,
            S3Port = S3Port,
            DataDirectory = DataDirectory,
            OperatorPassword = "secret",
            NodeToken = "changeme",
            S3Mode = mode,
            BindHttpListeners = false,
            RunReconcilers = false
        };
        AppComposition.Configure(builder, config);
        builder.Services.RemoveAll<ICephQueryProvider>();
        builder.Services.AddSingleton<ICephQueryProvider>(Ceph);
        builder.Services.RemoveAll<INooutProvider>();
        builder.Services.AddSingleton<INooutProvider>(new RecordingNooutProvider());
        builder.Services.RemoveAll<IProcessRunner>();
        builder.Services.AddSingleton<IProcessRunner>(new ScriptedProcessRunner());
        builder.Services.RemoveAll<IRgwObjectStore>();
        builder.Services.AddSingleton<IRgwObjectStore>(Rgw);
        builder.Services.RemoveAll<IRgwProxy>();
        builder.Services.AddSingleton<IRgwProxy>(Proxy);
        builder.WebHost.UseKestrel().UseUrls(
            $"http://127.0.0.1:{OperatorPort}",
            $"http://127.0.0.1:{S3Port}");
        _app = builder.Build();
    }

    public static async Task<TwoPortControlHost> StartAsync(S3AdmissionMode mode = S3AdmissionMode.Retry)
    {
        var host = new TwoPortControlHost(mode);
        await AppComposition.Initialize(host._app);
        await host._app.StartAsync();
        return host;
    }

    public HttpClient Operator(bool followRedirects = true)
        => new(new HttpClientHandler { AllowAutoRedirect = followRedirects })
        {
            BaseAddress = OperatorAddress,
            Timeout = TimeSpan.FromSeconds(10)
        };

    public HttpClient S3()
        => new() { BaseAddress = S3Address, Timeout = TimeSpan.FromSeconds(10) };

    public T Service<T>() where T : notnull => _app.Services.GetRequiredService<T>();

    /// <summary>
    /// Drives the plane to READY the way an operator would, so an S3 test can start from a
    /// serving appliance. The reconcilers are off, so nothing moves it back underneath the test.
    /// </summary>
    public TwoPortControlHost ReachReady()
    {
        var plane = Service<StoragePlaneService>();
        var operationId = OperationIdRules.Create().Value;
        plane.MarkObserved(StoragePlaneState.Cold, "startup-reconcile");
        plane.RequestWake(operationId, "operator");
        plane.EnterReady(operationId);
        return this;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        try
        {
            Directory.Delete(DataDirectory, recursive: true);
        }
        catch (Exception)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string FindControlRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Join(directory.FullName, "ColdCeph.Control");
            if (Directory.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("ColdCeph.Control project not found.");
    }
}

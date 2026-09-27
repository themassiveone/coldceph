using ColdCeph.Control.Features.Integrity.Interfaces;
using ColdCeph.Control.Features.StoragePlane.Interfaces;
using ColdCeph.Control.Shared;
using ColdCeph.Control.Tests.Fake;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ColdCeph.Control.Tests.Support;

public sealed class ControlAppFactory : WebApplicationFactory<Program>
{
    private readonly string _dataDir = Path.Join(Path.GetTempPath(), "coldceph-tests", Guid.NewGuid().ToString("N"));
    public FakeCephQueryProvider Ceph { get; } = new();

    public ControlAppFactory()
    {
        Environment.SetEnvironmentVariable("COLDCEPH_BIND", "0");
        Environment.SetEnvironmentVariable("COLDCEPH_DATA", _dataDir);
        Environment.SetEnvironmentVariable("COLDCEPH_OPERATOR_PASSWORD", "secret");
        Environment.SetEnvironmentVariable("COLDCEPH_NODE_TOKEN", "changeme");
        Environment.SetEnvironmentVariable("COLDCEPH_S3_MODE", "retry");
        Environment.SetEnvironmentVariable("COLDCEPH_NODE_ENDPOINT", null);
        Environment.SetEnvironmentVariable("COLDCEPH_CEPH_BINARY", null);
        Environment.SetEnvironmentVariable("COLDCEPH_CEPH_CONTAINER", null);
        Environment.SetEnvironmentVariable("COLDCEPH_CEPH_CONF", null);
        Environment.SetEnvironmentVariable("COLDCEPH_CEPH_KEYRING", null);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(FindControlRoot());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICephQueryProvider>();
            services.AddSingleton<ICephQueryProvider>(Ceph);
            services.RemoveAll<INooutProvider>();
            services.AddSingleton<INooutProvider>(new RecordingNooutProvider());
            services.RemoveAll<IProcessRunner>();
            services.AddSingleton<IProcessRunner>(new RecordingProcessRunner());
        });
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

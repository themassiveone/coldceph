using ColdCeph.Node.Composition;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ColdCeph.Node.Tests.Support;

public sealed class NodeAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("NodeToken", "secret");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<NodeConfig>();
            services.AddSingleton(new NodeConfig { NodeToken = "secret", HostId = "h1", Hostname = "h1" });
        });
    }
}

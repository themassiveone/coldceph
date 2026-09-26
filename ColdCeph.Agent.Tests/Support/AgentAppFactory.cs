using ColdCeph.Agent.Composition;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ColdCeph.Agent.Tests.Support;

public sealed class AgentAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("AgentToken", "secret");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<AgentConfig>();
            services.AddSingleton(new AgentConfig { AgentToken = "secret", HostId = "h1", Hostname = "h1" });
        });
    }
}

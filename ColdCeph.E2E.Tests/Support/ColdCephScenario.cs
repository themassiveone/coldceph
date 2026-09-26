using ColdCeph.Agent.Composition;
using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Integrity.Interfaces;
using ColdCeph.Control.Features.StoragePlane.Interfaces;
using ColdCeph.Control.Shared;
using ColdCeph.Control.Tests.Fake;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xcepto.Builder;
using Xcepto.Data;
using Xcepto.Scenarios;

namespace ColdCeph.E2E.Tests.Support;

public sealed class ColdCephScenario : XceptoScenario
{
    private WebApplication? _control;
    private WebApplication? _agent;
    public Uri ControlAddress { get; private set; } = new("http://127.0.0.1");
    public Uri AgentAddress { get; private set; } = new("http://127.0.0.1");

    protected override ScenarioSetup Setup(ScenarioSetupBuilder builder) => builder.Build();

    protected override ScenarioInitialization Initialize(ScenarioInitializationBuilder builder)
    {
        builder.Do(async _ =>
        {
            var data = Path.Join(Path.GetTempPath(), "coldceph-e2e", Guid.NewGuid().ToString("N"));
            var controlBuilder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development,
                ContentRootPath = FindProject("ColdCeph.Control")
            });
            var config = new ControlConfig
            {
                BindHttpListeners = false,
                DataDirectory = data,
                OperatorPassword = "secret",
                AgentToken = "secret",
                S3Mode = ColdCeph.Core.Features.S3.DTOs.S3AdmissionMode.Retry
            };
            AppComposition.Configure(controlBuilder, config);
            controlBuilder.Services.RemoveAll<ICephQueryProvider>();
            controlBuilder.Services.AddSingleton<ICephQueryProvider>(new FakeCephQueryProvider());
            controlBuilder.Services.RemoveAll<INooutProvider>();
            controlBuilder.Services.AddSingleton<INooutProvider>(new RecordingNooutProvider());
            controlBuilder.Services.RemoveAll<IProcessRunner>();
            controlBuilder.Services.AddSingleton<IProcessRunner>(new RecordingProcessRunner());
            controlBuilder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            _control = controlBuilder.Build();
            await AppComposition.Initialize(_control);
            await _control.StartAsync();
            ControlAddress = new Uri(FirstAddress(_control));

            var agentBuilder = WebApplication.CreateBuilder();
            AgentAppComposition.Configure(agentBuilder, new AgentConfig { AgentToken = "secret", HostId = "h1", Hostname = "h1" });
            agentBuilder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            _agent = agentBuilder.Build();
            await AgentAppComposition.Initialize(_agent);
            await _agent.StartAsync();
            AgentAddress = new Uri(FirstAddress(_agent));
        });
        return base.Initialize(builder);
    }

    protected override ScenarioCleanup Cleanup(ScenarioCleanupBuilder builder)
    {
        builder.Do(async _ =>
        {
            if (_control is not null)
                await _control.DisposeAsync();
            if (_agent is not null)
                await _agent.DisposeAsync();
        });
        return base.Cleanup(builder);
    }

    private static string FirstAddress(WebApplication app)
        => app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.First()
           ?? throw new InvalidOperationException("No bound address.");

    private static string FindProject(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Join(directory.FullName, name);
            if (Directory.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new InvalidOperationException(name);
    }
}

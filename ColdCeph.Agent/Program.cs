using ColdCeph.Agent.Composition;

var builder = WebApplication.CreateBuilder(args);
var config = AgentConfig.FromEnvironment();
builder.WebHost.UseUrls([.. config.ListenUrls()]);
AgentAppComposition.Configure(builder, config);
var app = builder.Build();
await AgentAppComposition.Initialize(app);
await app.RunAsync();

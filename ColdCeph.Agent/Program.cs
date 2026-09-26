using ColdCeph.Agent.Composition;

var builder = WebApplication.CreateBuilder(args);
var config = AgentConfig.FromEnvironment();
builder.WebHost.UseUrls($"http://127.0.0.1:{config.Port}");
AgentAppComposition.Configure(builder, config);
var app = builder.Build();
await AgentAppComposition.Initialize(app);
await app.RunAsync();

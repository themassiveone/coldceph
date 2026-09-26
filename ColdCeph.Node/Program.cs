using ColdCeph.Node.Composition;

var builder = WebApplication.CreateBuilder(args);
var config = NodeConfig.FromEnvironment();
builder.WebHost.UseUrls([.. config.ListenUrls()]);
NodeAppComposition.Configure(builder, config);
var app = builder.Build();
await NodeAppComposition.Initialize(app);
await app.RunAsync();

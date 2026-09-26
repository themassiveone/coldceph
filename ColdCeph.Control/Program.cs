using ColdCeph.Control.Composition;

var builder = WebApplication.CreateBuilder(args);
var config = ControlConfig.FromEnvironment();
if (config.BindHttpListeners)
    builder.WebHost.UseUrls($"http://127.0.0.1:{config.OperatorPort}", $"http://127.0.0.1:{config.S3Port}");

AppComposition.Configure(builder, config);
var app = builder.Build();
await AppComposition.Initialize(app);
await app.RunAsync();

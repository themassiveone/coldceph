using ColdCeph.Control.Composition;

var builder = WebApplication.CreateBuilder(args);
var config = ControlConfig.FromEnvironment();
if (config.BindHttpListeners)
    builder.WebHost.UseUrls([.. config.ListenUrls()]);

AppComposition.Configure(builder, config);
var app = builder.Build();
await AppComposition.Initialize(app);
await app.RunAsync();

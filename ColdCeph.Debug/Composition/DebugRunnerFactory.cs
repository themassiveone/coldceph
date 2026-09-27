using ColdCeph.Debug.Features.Compose.Controllers;
using ColdCeph.Debug.Features.Compose.Providers;
using ColdCeph.Debug.Features.Compose.Services;
using ColdCeph.Debug.Features.Host.Controllers;
using ColdCeph.Debug.Features.Host.Providers;
using ColdCeph.Debug.Features.Host.Services;
using ColdCeph.Debug.Features.Pages.Controllers;
using ColdCeph.Debug.Features.Pages.Providers;
using ColdCeph.Debug.Features.Pages.Services;

namespace ColdCeph.Debug.Composition;

public static class DebugRunnerFactory
{
    public static HostController Create()
    {
        var catalog = new OperatorPageCatalog();
        var pages = new PagesController(
            new PageCaptureService(catalog, new OperatorHtmlSnapshot(), new ChromiumPageDriver()),
            new OperatorAllowService());
        var health = new HttpControlHealth();
        var compose = new ComposeController(
            new ComposeStackService(new DockerComposeCli(), health, new DotnetControlProcess(health)));
        return new HostController(new DebugArgParser(), new HostCommandService(pages, compose));
    }
}

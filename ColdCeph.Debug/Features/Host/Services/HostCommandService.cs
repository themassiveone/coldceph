using ColdCeph.Debug.Features.Host.DTOs;
using ColdCeph.Debug.Features.Pages.Controllers;

namespace ColdCeph.Debug.Features.Host.Services;

public sealed class HostCommandService
{
    private readonly PagesController _pages;

    public HostCommandService(PagesController pages)
    {
        _pages = pages;
    }

    public Task<int> ExecuteAsync(DebugCommandDto command, CancellationToken cancellationToken)
        => command.Verb switch
        {
            "pages" => Task.FromResult(_pages.WriteCatalog()),
            "screenshot" => _pages.ScreenshotAsync(command.PagePath, cancellationToken),
            _ => throw new ArgumentException($"Unknown command '{command.Verb}'.")
        };
}

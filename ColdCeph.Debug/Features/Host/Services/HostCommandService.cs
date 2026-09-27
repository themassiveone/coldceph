using ColdCeph.Debug.Features.Compose.Controllers;
using ColdCeph.Debug.Features.Host.DTOs;
using ColdCeph.Debug.Features.Pages.Controllers;

namespace ColdCeph.Debug.Features.Host.Services;

public sealed class HostCommandService
{
    private readonly PagesController _pages;
    private readonly ComposeController _compose;

    public HostCommandService(PagesController pages, ComposeController compose)
    {
        _pages = pages;
        _compose = compose;
    }

    public Task<int> ExecuteAsync(DebugCommandDto command, CancellationToken cancellationToken)
        => command.Verb switch
        {
            "pages" => Task.FromResult(_pages.WriteCatalog()),
            "screenshot" => _pages.ScreenshotAsync(command.PagePath, cancellationToken),
            "up" => Task.FromResult(_compose.Up()),
            "down" => Task.FromResult(_compose.Down()),
            "status" => Task.FromResult(_compose.Status()),
            "allow" => _pages.AllowAsync(command.HostId, cancellationToken),
            _ => throw new ArgumentException($"Unknown command '{command.Verb}'.")
        };
}

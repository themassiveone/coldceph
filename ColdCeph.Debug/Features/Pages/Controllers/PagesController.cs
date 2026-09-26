using ColdCeph.Debug.Features.Pages.Services;

namespace ColdCeph.Debug.Features.Pages.Controllers;

public sealed class PagesController
{
    private readonly PageCaptureService _capture;

    public PagesController(PageCaptureService capture)
    {
        _capture = capture;
    }

    public int WriteCatalog()
    {
        foreach (var line in _capture.ListPaths())
            Console.WriteLine(line);
        return 0;
    }

    public async Task<int> ScreenshotAsync(string? path, CancellationToken cancellationToken)
    {
        var files = await _capture.CaptureAsync(path, cancellationToken);
        foreach (var file in files)
            Console.WriteLine($"screenshot: {file}");
        return 0;
    }
}

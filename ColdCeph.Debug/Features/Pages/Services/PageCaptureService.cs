using ColdCeph.Debug.Features.Pages.Interfaces;
using ColdCeph.Debug.Features.Pages.Services;

namespace ColdCeph.Debug.Features.Pages.Services;

public sealed class PageCaptureService
{
    private readonly OperatorPageCatalog _catalog;
    private readonly IHtmlSnapshot _html;
    private readonly IPageCapture _capture;

    public PageCaptureService(OperatorPageCatalog catalog, IHtmlSnapshot html, IPageCapture capture)
    {
        _catalog = catalog;
        _html = html;
        _capture = capture;
    }

    public IReadOnlyList<string> ListPaths() => _catalog.All.Select(page => $"{page.Path}  {page.Name}").ToArray();

    public async Task<IReadOnlyList<string>> CaptureAsync(string? path, CancellationToken cancellationToken)
    {
        var pages = path is null ? _catalog.All : [_catalog.Resolve(path)];
        var outputRoot = OutputRoot();
        Directory.CreateDirectory(outputRoot);
        var written = new List<string>();
        foreach (var page in pages)
        {
            var slug = page.Path == "/" ? "storage" : page.Path.Trim('/').Replace('/', '-');
            var htmlPath = Path.Join(outputRoot, slug + ".html");
            var pngPath = Path.Join(outputRoot, slug + ".png");
            var pageUri = new Uri(OperatorUrl(), page.Path);
            Console.WriteLine($"capturing: {page.Path}");
            await _html.SaveAsync(pageUri, page.Anonymous, htmlPath, cancellationToken);
            await _capture.CaptureFileAsync(htmlPath, pngPath, cancellationToken);
            written.Add(pngPath);
        }

        return written;
    }

    private static Uri OperatorUrl()
    {
        var configured = Environment.GetEnvironmentVariable("COLDCEPH_OPERATOR_URL");
        if (!string.IsNullOrWhiteSpace(configured))
            return new Uri(configured.TrimEnd('/') + "/", UriKind.Absolute);
        var port = Environment.GetEnvironmentVariable("WEB_PORT") ?? "8080";
        return new Uri($"http://127.0.0.1:{port}/");
    }

    private static string OutputRoot()
    {
        var start = Directory.GetCurrentDirectory();
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Join(directory.FullName, "coldceph.slnx")))
                return Path.Join(directory.FullName, ".git", "coldceph", "debug");
            directory = directory.Parent;
        }

        return Path.Join(start, ".git", "coldceph", "debug");
    }
}

using ColdCeph.Debug.Features.Pages.Interfaces;
using ColdCeph.Debug.Features.Pages.Services;

namespace ColdCeph.Debug.Tests.Features.Pages.Unit;

[TestFixture]
public sealed class PageCaptureServiceTests
{
    [Test]
    public async Task Capture_refuses_an_unknown_page_before_talking_to_control()
    {
        var html = new RecordingSnapshot();
        var capture = new RecordingCapture();
        var service = new PageCaptureService(new OperatorPageCatalog(), html, capture);

        Assert.That(
            async () => await service.CaptureAsync("/not-a-page", CancellationToken.None),
            Throws.InstanceOf<ArgumentException>());
        Assert.That(html.Calls, Is.Empty);
        Assert.That(capture.Calls, Is.Empty);
    }

    [Test]
    public void List_does_not_include_api_routes()
    {
        var service = new PageCaptureService(new OperatorPageCatalog(), new RecordingSnapshot(), new RecordingCapture());

        Assert.That(service.ListPaths().Any(line => line.Contains("/v1", StringComparison.Ordinal)), Is.False);
        Assert.That(service.ListPaths().Any(line => line.StartsWith("/", StringComparison.Ordinal)), Is.True);
    }

    private sealed class RecordingSnapshot : IHtmlSnapshot
    {
        public List<Uri> Calls { get; } = [];

        public Task<string> SaveAsync(Uri page, bool anonymous, string destination, CancellationToken cancellationToken)
        {
            Calls.Add(page);
            return Task.FromResult(destination);
        }
    }

    private sealed class RecordingCapture : IPageCapture
    {
        public List<string> Calls { get; } = [];

        public Task CaptureFileAsync(string htmlPath, string pngPath, CancellationToken cancellationToken)
        {
            Calls.Add(pngPath);
            return Task.CompletedTask;
        }
    }
}

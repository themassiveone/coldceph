namespace ColdCeph.Debug.Features.Pages.Interfaces;

public interface IHtmlSnapshot
{
    Task<string> SaveAsync(Uri page, bool anonymous, string destination, CancellationToken cancellationToken);
}

public interface IPageCapture
{
    Task CaptureFileAsync(string htmlPath, string pngPath, CancellationToken cancellationToken);
}

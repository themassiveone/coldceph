using ColdCeph.Debug.Features.Pages.Interfaces;

namespace ColdCeph.Debug.Features.Pages.Providers;

public sealed class ChromiumPageDriver : IPageCapture
{
    public async Task CaptureFileAsync(string htmlPath, string pngPath, CancellationToken cancellationToken)
    {
        var chromium = FindChromium();
        Directory.CreateDirectory(Path.GetDirectoryName(pngPath)!);
        var url = new Uri(Path.GetFullPath(htmlPath)).AbsoluteUri;
        using var process = new System.Diagnostics.Process();
        process.StartInfo = chromium is null
            ? NixChromium(url, pngPath)
            : DirectChromium(chromium, url, pngPath);
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.Start();
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0 || !File.Exists(pngPath))
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException($"Chromium screenshot failed: {error}");
        }
    }

    private static System.Diagnostics.ProcessStartInfo DirectChromium(string chromium, string url, string pngPath)
    {
        var info = new System.Diagnostics.ProcessStartInfo
        {
            FileName = chromium
        };
        foreach (var argument in ChromiumArguments(url, pngPath))
            info.ArgumentList.Add(argument);
        return info;
    }

    private static System.Diagnostics.ProcessStartInfo NixChromium(string url, string pngPath)
    {
        var quoted = string.Join(' ', ChromiumArguments(url, pngPath).Select(argument =>
            "'" + argument.Replace("'", "'\\''") + "'"));
        return new System.Diagnostics.ProcessStartInfo
        {
            FileName = "nix-shell",
            ArgumentList = { "-p", "chromium", "--run", $"chromium {quoted}" }
        };
    }

    private static string[] ChromiumArguments(string url, string pngPath)
        =>
        [
            "--headless=new",
            "--disable-gpu",
            "--no-sandbox",
            "--hide-scrollbars",
            "--window-size=1440,900",
            $"--screenshot={pngPath}",
            url
        ];

    private static string? FindChromium()
    {
        foreach (var name in new[] { "chromium", "chromium-browser", "google-chrome" })
        {
            var found = Environment.GetEnvironmentVariable("PATH")
                ?.Split(Path.PathSeparator)
                .Select(directory => Path.Join(directory, name))
                .FirstOrDefault(File.Exists);
            if (found is not null)
                return found;
        }

        return null;
    }
}

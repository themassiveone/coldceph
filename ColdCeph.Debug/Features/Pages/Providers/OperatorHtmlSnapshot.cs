using System.Net;
using System.Text.RegularExpressions;
using ColdCeph.Debug.Features.Pages.Interfaces;

namespace ColdCeph.Debug.Features.Pages.Providers;

public sealed class OperatorHtmlSnapshot : IHtmlSnapshot
{
    public async Task<string> SaveAsync(Uri page, bool anonymous, string destination, CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        if (!anonymous)
            await SignInAsync(client, page, cancellationToken);

        using var response = await client.GetAsync(page, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found)
            throw new InvalidOperationException($"Control sent {page} to {response.Headers.Location}. Sign-in failed.");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var cssUri = new Uri(page, "/css/operator.css");
        using var cssResponse = await client.GetAsync(cssUri, cancellationToken);
        var css = cssResponse.IsSuccessStatusCode
            ? await cssResponse.Content.ReadAsStringAsync(cancellationToken)
            : string.Empty;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllTextAsync(destination, InlineCss(html, css), cancellationToken);
        return destination;
    }

    public static string InlineCss(string html, string css)
    {
        var styled = Regex.Replace(
            html,
            """<link\s+rel="stylesheet"\s+href="/css/operator.css"\s*/>""",
            "<style>" + css + "</style>",
            RegexOptions.IgnoreCase);
        if (styled == html)
            throw new InvalidOperationException("Operator HTML did not link /css/operator.css.");
        return styled;
    }

    private static async Task SignInAsync(HttpClient client, Uri page, CancellationToken cancellationToken)
    {
        var login = new Uri(page, "/auth/login");
        var html = await client.GetStringAsync(login, cancellationToken);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        if (!match.Success)
            throw new InvalidOperationException("Login page has no antiforgery token.");
        var password = Environment.GetEnvironmentVariable("COLDCEPH_OPERATOR_PASSWORD") ?? "changeme";
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["password"] = password,
            ["__RequestVerificationToken"] = match.Groups[1].Value
        });
        using var response = await client.PostAsync(login, content, cancellationToken);
        if (response.StatusCode is not (HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.OK))
            throw new InvalidOperationException($"Login failed: {(int)response.StatusCode}");
    }
}

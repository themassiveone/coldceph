using System.Text.RegularExpressions;

namespace ColdCeph.Debug.Features.Pages.Services;

public static class HostsPageForms
{
    public static IReadOnlyList<string> PendingApproveIds(string html)
        => Regex.Matches(html, "action=\"/hosts/([^\"]+)/approve\"")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public static string AntiforgeryToken(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        if (!match.Success)
            throw new InvalidOperationException("Hosts page has no antiforgery token.");
        return match.Groups[1].Value;
    }
}

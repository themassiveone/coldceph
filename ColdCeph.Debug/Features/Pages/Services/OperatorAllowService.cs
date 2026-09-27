using System.Net;
using ColdCeph.Debug.Shared;

namespace ColdCeph.Debug.Features.Pages.Services;

public sealed class OperatorAllowService
{
    public async Task<IReadOnlyList<string>> AllowAsync(string? hostId, CancellationToken cancellationToken)
    {
        var origin = OperatorEndpoint.Url();
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        await SignInAsync(client, origin, cancellationToken);
        var hosts = new Uri(origin, "/hosts");
        var html = await client.GetStringAsync(hosts, cancellationToken);
        var pending = HostsPageForms.PendingApproveIds(html);
        var selected = string.IsNullOrWhiteSpace(hostId)
            ? pending
            : pending.Where(id => string.Equals(id, hostId, StringComparison.Ordinal)).ToArray();
        if (selected.Count == 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(hostId)
                ? "No pending join requests."
                : $"No pending join for '{hostId}'.");
        }

        var token = HostsPageForms.AntiforgeryToken(html);
        foreach (var id in selected)
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token
            });
            using var response = await client.PostAsync(new Uri(origin, $"/hosts/{id}/approve"), content, cancellationToken);
            if (response.StatusCode is not (HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.OK))
                throw new InvalidOperationException($"Allow {id} failed: {(int)response.StatusCode}");
        }

        return selected;
    }

    private static async Task SignInAsync(HttpClient client, Uri origin, CancellationToken cancellationToken)
    {
        var login = new Uri(origin, "/auth/login");
        var html = await client.GetStringAsync(login, cancellationToken);
        var token = HostsPageForms.AntiforgeryToken(html);
        var password = Environment.GetEnvironmentVariable("COLDCEPH_OPERATOR_PASSWORD") ?? "changeme";
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["password"] = password,
            ["__RequestVerificationToken"] = token
        });
        using var response = await client.PostAsync(login, content, cancellationToken);
        if (response.StatusCode is not (HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.OK))
            throw new InvalidOperationException($"Login failed: {(int)response.StatusCode}");
    }
}

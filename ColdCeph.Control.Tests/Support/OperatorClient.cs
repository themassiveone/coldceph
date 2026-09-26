using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ColdCeph.Control.Tests.Support;

public static class OperatorClient
{
    public static async Task<HttpClient> SignedIn(ControlAppFactory factory, string password = "secret")
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await client.GetStringAsync("/auth/login");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["password"] = password,
            ["__RequestVerificationToken"] = AntiforgeryToken(login)
        });
        var response = await client.PostAsync("/auth/login", content);
        if (response.StatusCode != HttpStatusCode.Redirect)
            throw new InvalidOperationException($"Login failed: {(int)response.StatusCode}");
        return client;
    }

    public static string AntiforgeryToken(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        if (!match.Success)
            throw new InvalidOperationException("Login page has no antiforgery token.");
        return match.Groups[1].Value;
    }
}

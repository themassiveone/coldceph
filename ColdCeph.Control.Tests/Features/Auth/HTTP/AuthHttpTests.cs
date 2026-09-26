using System.Net;
using System.Text.RegularExpressions;

namespace ColdCeph.Control.Tests.Features.Auth.HTTP;

[TestFixture]
public sealed class AuthHttpTests
{
    [Test]
    public async Task Operator_pages_return_401_without_a_login_redirect()
    {
        using var factory = new Support.ControlAppFactory();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(response.Headers.Location, Is.Null);
    }

    [Test]
    public async Task Login_post_redirects_home_when_password_matches()
    {
        using var factory = new Support.ControlAppFactory();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var loginPage = await client.GetStringAsync("/auth/login");
        var token = Regex.Match(loginPage, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["password"] = "secret",
            ["__RequestVerificationToken"] = token
        });

        var response = await client.PostAsync("/auth/login", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(response.Headers.Location?.ToString(), Is.EqualTo("/"));
    }
}

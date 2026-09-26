using System.Net;
using System.Text.RegularExpressions;

namespace ColdCeph.Control.Tests.Features.Auth.HTTP;

[TestFixture]
public sealed class AuthHttpNegativeTests
{
    [Test]
    public async Task Login_post_with_wrong_password_returns_401()
    {
        using var factory = new Support.ControlAppFactory();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var loginPage = await client.GetStringAsync("/auth/login");
        var token = Regex.Match(loginPage, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["password"] = "wrong",
            ["__RequestVerificationToken"] = token
        });

        var response = await client.PostAsync("/auth/login", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(response.Headers.Location, Is.Null);
        Assert.That(factory.Ceph.HealthDetailCalls, Is.EqualTo(0));
    }
}

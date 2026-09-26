namespace ColdCeph.Control.Tests.Features.Auth.HTTP;

[TestFixture]
public sealed class AuthPagesHttpTests
{
    [Test]
    public async Task Login_does_not_auto_refresh_or_query_ceph()
    {
        using var factory = new Support.ControlAppFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/auth/login");

        Assert.That(html, Does.Contain("Operator login"));
        Assert.That(html, Does.Not.Contain("http-equiv=\"refresh\""));
        Assert.That(html, Does.Not.Contain("Operational"));
        Assert.That(factory.Ceph.HealthDetailCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task Login_does_not_show_operator_navigation()
    {
        using var factory = new Support.ControlAppFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/auth/login");

        Assert.That(html, Does.Not.Contain("href=\"/osds\""));
        Assert.That(html, Does.Not.Contain("href=\"/hosts\""));
        Assert.That(html, Does.Contain("name=\"password\""));
        Assert.That(html, Does.Contain("/css/operator.css"));
    }

    [Test]
    public async Task Operator_stylesheet_is_served()
    {
        using var factory = new Support.ControlAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/css/operator.css");
        var css = await response.Content.ReadAsStringAsync();

        Assert.That(response.IsSuccessStatusCode, Is.True);
        Assert.That(css, Does.Contain("--cc-canvas"));
    }

    [Test]
    public async Task Missing_static_file_is_not_the_operator_stylesheet()
    {
        using var factory = new Support.ControlAppFactory();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/css/missing.css");

        Assert.That(response.IsSuccessStatusCode, Is.False);
    }
}

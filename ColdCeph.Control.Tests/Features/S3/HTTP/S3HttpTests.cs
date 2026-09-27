using System.Net;

namespace ColdCeph.Control.Tests.Features.S3.HTTP;

[TestFixture]
public sealed class S3HttpTests
{
    [Test]
    public async Task Cold_get_in_retry_mode_returns_503()
    {
        using var factory = new Support.ControlAppFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/bucket/object");
        request.Headers.Add("X-ColdCeph-S3", "1");

        var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(response.Headers.RetryAfter, Is.Not.Null);
        Assert.That(factory.Ceph.HealthDetailCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task Operator_html_port_does_not_proxy_unauthenticated_s3_without_header()
    {
        using var factory = new Support.ControlAppFactory();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/bucket/object");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(response.Headers.RetryAfter, Is.Null);
    }
}

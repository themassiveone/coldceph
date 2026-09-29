using System.Net;
using ColdCeph.Control.Tests.Support;
using ColdCeph.Core.Features.S3.DTOs;

namespace ColdCeph.Control.Tests.Features.S3.HTTP;

/// <summary>
/// S3 admission over the real two-listener topology. Dispatch is by port, so these bind both.
/// </summary>
[TestFixture]
public sealed class S3HttpTests
{
    [Test]
    public async Task Cold_get_in_retry_mode_returns_503_without_confirming_ceph()
    {
        await using var host = await TwoPortControlHost.StartAsync(S3AdmissionMode.Retry);
        using var client = host.S3();

        var response = await client.GetAsync("/bucket/object");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(response.Headers.RetryAfter, Is.Not.Null);
        Assert.That(host.Ceph.ObservationCalls, Is.EqualTo(0));
        Assert.That(host.Proxy.Calls, Is.EqualTo(0));
    }

    [Test]
    public async Task A_ready_plane_forwards_to_rgw_after_one_confirmation()
    {
        await using var host = await TwoPortControlHost.StartAsync(S3AdmissionMode.Retry);
        host.ReachReady();
        using var client = host.S3();

        var response = await client.GetAsync("/bucket/object");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(host.Proxy.Calls, Is.EqualTo(1));
        Assert.That(host.Ceph.ObservationCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task A_ready_plane_refuses_writes_when_the_confirmation_is_not_write_ready()
    {
        await using var host = await TwoPortControlHost.StartAsync(S3AdmissionMode.Retry);
        host.Ceph.Seeing(CephFixture.Recovering);
        host.ReachReady();
        using var client = host.S3();

        var response = await client.PutAsync("/bucket/object", new StringContent("payload"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(host.Proxy.Calls, Is.EqualTo(0));
    }

    [Test]
    public async Task A_ready_plane_still_serves_reads_while_recovering()
    {
        await using var host = await TwoPortControlHost.StartAsync(S3AdmissionMode.Retry);
        host.Ceph.Seeing(CephFixture.Recovering);
        host.ReachReady();
        using var client = host.S3();

        var response = await client.GetAsync("/bucket/object");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task A_durability_failure_refuses_reads_and_writes()
    {
        await using var host = await TwoPortControlHost.StartAsync(S3AdmissionMode.Retry);
        host.Ceph.Seeing(CephFixture.Unfound);
        host.ReachReady();
        using var client = host.S3();

        var read = await client.GetAsync("/bucket/object");
        var write = await client.PutAsync("/bucket/object", new StringContent("payload"));

        Assert.That(read.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(write.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(host.Proxy.Calls, Is.EqualTo(0));
    }

    // ---- the two listeners stay separate ------------------------------------

    [Test]
    public async Task The_operator_port_does_not_proxy_s3_paths()
    {
        await using var host = await TwoPortControlHost.StartAsync();
        using var client = host.Operator(followRedirects: false);

        var response = await client.GetAsync("/bucket/object");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(response.Headers.RetryAfter, Is.Null);
        Assert.That(host.Proxy.Calls, Is.EqualTo(0));
    }

    /// <summary>
    /// There used to be an "X-ColdCeph-S3" escape hatch in production so a single-port test host
    /// could reach the gateway. Any client sending it to the operator port skipped MVC, cookie
    /// auth and antiforgery entirely. No header selects the gateway now.
    /// </summary>
    [Test]
    public async Task No_header_can_turn_an_operator_port_request_into_an_s3_request()
    {
        await using var host = await TwoPortControlHost.StartAsync();
        using var client = host.Operator(followRedirects: false);

        foreach (var header in new[] { "X-ColdCeph-S3", "X-Coldceph-S3", "x-coldceph-s3" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/bucket/object");
            request.Headers.TryAddWithoutValidation(header, "1");

            var response = await client.SendAsync(request);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), header);
            Assert.That(response.Headers.RetryAfter, Is.Null, header);
        }

        Assert.That(host.Proxy.Calls, Is.EqualTo(0));
    }

    [Test]
    public async Task The_s3_port_does_not_serve_operator_html()
    {
        await using var host = await TwoPortControlHost.StartAsync();
        using var client = host.S3();

        var response = await client.GetAsync("/auth/login");

        // Everything on the S3 listener is an S3 request, so a cold plane answers 503 rather
        // than rendering a login page.
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        var body = await response.Content.ReadAsStringAsync();
        Assert.That(body, Does.Not.Contain("<form"));
    }

    [Test]
    public async Task Health_stays_anonymous_on_the_operator_port()
    {
        await using var host = await TwoPortControlHost.StartAsync();
        using var client = host.Operator();

        var response = await client.GetAsync("/health");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("Cold"));
    }
}

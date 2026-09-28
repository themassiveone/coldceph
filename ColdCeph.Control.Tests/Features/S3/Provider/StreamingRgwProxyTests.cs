using System.Net;
using System.Text;
using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.S3.Providers;
using ColdCeph.Control.Tests.Support;
using Microsoft.AspNetCore.Http;

namespace ColdCeph.Control.Tests.Features.S3.Provider;

/// <summary>
/// The proxy had no tests. Everything a SigV4 signature covers has to arrive at RGW unchanged, so
/// these assert the bytes that go on the wire.
/// </summary>
[TestFixture]
public sealed class StreamingRgwProxyTests
{
    // ---- signed request elements -------------------------------------------

    [Test]
    public async Task Method_path_and_query_are_preserved()
    {
        var (proxy, handler, context) = Create("GET", "/cold/photos/summer.jpg", "?versionId=7&x-id=GetObject");

        await proxy.ProxyAsync(context);

        Assert.That(handler.LastRequest!.Method.Method, Is.EqualTo("GET"));
        Assert.That(handler.LastRequest.RequestUri!.AbsolutePath, Is.EqualTo("/cold/photos/summer.jpg"));
        Assert.That(handler.LastRequest.RequestUri.Query, Is.EqualTo("?versionId=7&x-id=GetObject"));
    }

    [Test]
    public async Task Host_and_authorization_are_forwarded()
    {
        var (proxy, handler, context) = Create("GET", "/cold/object");
        context.Request.Headers["Host"] = "s3.example.test:7480";
        context.Request.Headers["Authorization"] = "AWS4-HMAC-SHA256 Credential=coldceph/20260928/us-east-1/s3/aws4_request";

        await proxy.ProxyAsync(context);

        Assert.That(handler.HeaderValues("Host"), Does.Contain("s3.example.test:7480"));
        Assert.That(handler.HeaderValues("Authorization").Single(), Does.StartWith("AWS4-HMAC-SHA256"));
    }

    [Test]
    public async Task Amz_headers_are_forwarded()
    {
        var (proxy, handler, context) = Create("GET", "/cold/object");
        context.Request.Headers["x-amz-content-sha256"] = "UNSIGNED-PAYLOAD";
        context.Request.Headers["x-amz-date"] = "20260928T120000Z";
        context.Request.Headers["X-Amz-Server-Side-Encryption"] = "AES256";

        await proxy.ProxyAsync(context);

        Assert.That(handler.HeaderValues("x-amz-content-sha256"), Does.Contain("UNSIGNED-PAYLOAD"));
        Assert.That(handler.HeaderValues("x-amz-date"), Does.Contain("20260928T120000Z"));
        Assert.That(handler.HeaderValues("X-Amz-Server-Side-Encryption"), Does.Contain("AES256"));
    }

    [Test]
    public async Task Unrelated_headers_are_not_forwarded()
    {
        var (proxy, handler, context) = Create("GET", "/cold/object");
        context.Request.Headers["Cookie"] = "session=secret";
        context.Request.Headers["User-Agent"] = "aws-cli/2.0";

        await proxy.ProxyAsync(context);

        Assert.That(handler.HasHeader("Cookie"), Is.False);
    }

    // ---- the regression: Content-* on a body request -------------------------

    /// <summary>
    /// Headers used to be copied before the body was attached. Content headers belong to the
    /// content, so <c>HttpRequestMessage.Headers</c> rejects them and the fallback onto
    /// <c>Content.Headers</c> was skipped because <c>Content</c> was still null. Every
    /// <c>Content-*</c> header was dropped, including the signed <c>Content-MD5</c> — so RGW
    /// rejected every upload with a 403 and no test noticed.
    /// </summary>
    [Test]
    public async Task Put_preserves_the_signed_content_md5()
    {
        var (proxy, handler, context) = Create("PUT", "/cold/object", body: "payload");
        context.Request.Headers["Content-MD5"] = "rL0Y20zC+Fzt72VPzMSk2A==";

        await proxy.ProxyAsync(context);

        Assert.That(handler.HeaderValues("Content-MD5"), Does.Contain("rL0Y20zC+Fzt72VPzMSk2A=="));
    }

    [Test]
    public async Task Put_preserves_content_type()
    {
        var (proxy, handler, context) = Create("PUT", "/cold/object", body: "payload");
        context.Request.ContentType = "image/jpeg";

        await proxy.ProxyAsync(context);

        Assert.That(handler.HeaderValues("Content-Type"), Does.Contain("image/jpeg"));
    }

    [Test]
    public async Task Put_preserves_content_encoding()
    {
        var (proxy, handler, context) = Create("PUT", "/cold/object", body: "payload");
        context.Request.Headers["Content-Encoding"] = "aws-chunked";

        await proxy.ProxyAsync(context);

        Assert.That(handler.HeaderValues("Content-Encoding"), Does.Contain("aws-chunked"));
    }

    [Test]
    public async Task Put_forwards_the_body()
    {
        var (proxy, handler, context) = Create("PUT", "/cold/object", body: "the object bytes");

        await proxy.ProxyAsync(context);

        Assert.That(handler.LastBody, Is.EqualTo("the object bytes"));
    }

    /// <summary>
    /// Content-Length is framing: HttpClient sets it from the content it actually sends, and
    /// forwarding the inbound value can contradict that, which RGW reads as a truncated body.
    /// </summary>
    [Test]
    public async Task Put_does_not_forward_the_inbound_content_length()
    {
        var (proxy, handler, context) = Create("PUT", "/cold/object", body: "payload");
        context.Request.Headers["Content-Length"] = "999999";

        await proxy.ProxyAsync(context);

        Assert.That(handler.HeaderValues("Content-Length"), Does.Not.Contain("999999"));
    }

    [Test]
    public async Task A_get_sends_no_body()
    {
        var (proxy, handler, context) = Create("GET", "/cold/object");

        await proxy.ProxyAsync(context);

        Assert.That(handler.LastRequest!.Content, Is.Null);
    }

    /// <summary>Uploads should not be buffered whole before forwarding.</summary>
    [Test]
    public async Task Expect_100_continue_is_set_as_a_request_property()
    {
        var (proxy, handler, context) = Create("PUT", "/cold/object", body: "payload");
        context.Request.Headers["Expect"] = "100-continue";

        await proxy.ProxyAsync(context);

        Assert.That(handler.LastRequest!.Headers.ExpectContinue, Is.True);
    }

    [Test]
    public async Task Expect_continue_is_not_set_when_the_client_did_not_ask()
    {
        var (proxy, handler, context) = Create("PUT", "/cold/object", body: "payload");

        await proxy.ProxyAsync(context);

        Assert.That(handler.LastRequest!.Headers.ExpectContinue, Is.Not.True);
    }

    // ---- response ----------------------------------------------------------

    [Test]
    public async Task The_rgw_status_is_passed_back()
    {
        var (proxy, _, context) = Create("GET", "/cold/missing", respond: HttpStatusCode.NotFound);

        await proxy.ProxyAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task An_rgw_signature_rejection_is_passed_back_rather_than_masked()
    {
        var (proxy, _, context) = Create("PUT", "/cold/object", body: "payload", respond: HttpStatusCode.Forbidden);

        await proxy.ProxyAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(403));
    }

    [Test]
    public async Task Response_headers_are_passed_back()
    {
        var handler = new StubHttpHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("body", Encoding.UTF8, "image/jpeg")
            };
            response.Headers.TryAddWithoutValidation("x-amz-request-id", "tx000001");
            response.Headers.TryAddWithoutValidation("ETag", "\"abc123\"");
            response.Headers.TryAddWithoutValidation("Transfer-Encoding", "chunked");
            return response;
        });
        var (proxy, context) = CreateWith(handler, "GET", "/cold/object");

        await proxy.ProxyAsync(context);

        Assert.That(context.Response.Headers["x-amz-request-id"].ToString(), Is.EqualTo("tx000001"));
        Assert.That(context.Response.Headers["ETag"].ToString(), Is.EqualTo("\"abc123\""));
        Assert.That(context.Response.Headers.ContainsKey("Content-Type"), Is.True);
    }

    /// <summary>Kestrel frames the response; passing RGW's framing through corrupts it.</summary>
    [Test]
    public async Task Transfer_encoding_is_not_passed_back()
    {
        var handler = new StubHttpHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("body") };
            response.Headers.TryAddWithoutValidation("Transfer-Encoding", "chunked");
            return response;
        });
        var (proxy, context) = CreateWith(handler, "GET", "/cold/object");

        await proxy.ProxyAsync(context);

        Assert.That(context.Response.Headers.ContainsKey("Transfer-Encoding"), Is.False);
    }

    [Test]
    public async Task The_response_body_is_streamed_back()
    {
        var (proxy, _, context) = Create("GET", "/cold/object", respondBody: "the stored bytes");

        await proxy.ProxyAsync(context);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        Assert.That(await reader.ReadToEndAsync(), Is.EqualTo("the stored bytes"));
    }

    private static (StreamingRgwProxy Proxy, StubHttpHandler Handler, HttpContext Context) Create(
        string method,
        string path,
        string query = "",
        string? body = null,
        HttpStatusCode respond = HttpStatusCode.OK,
        string respondBody = "ok")
    {
        var handler = StubHttpHandler.Returning(respond, respondBody, "application/octet-stream");
        var (proxy, context) = CreateWith(handler, method, path, query, body);
        return (proxy, handler, context);
    }

    private static (StreamingRgwProxy Proxy, HttpContext Context) CreateWith(
        StubHttpHandler handler,
        string method,
        string path,
        string query = "",
        string? body = null)
    {
        var proxy = new StreamingRgwProxy(
            new ControlConfig { RgwEndpoint = new Uri("http://rgw.test:7481") },
            handler.AsFactory());

        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        if (body is not null)
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Response.Body = new MemoryStream();
        return (proxy, context);
    }
}

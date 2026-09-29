using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.S3.Interfaces;

namespace ColdCeph.Control.Features.S3.Providers;

/// <summary>
/// Forwards an S3 request to RGW without buffering it.
/// <para>
/// The signature covers the method, path, query, <c>Host</c> and the signed headers, so every one
/// of them has to arrive at RGW byte-identical. In particular the body must be attached
/// <em>before</em> headers are copied: <c>Content-*</c> headers belong to the content, not the
/// request, so copying them while <c>Content</c> is still null silently dropped all of them —
/// including the signed <c>Content-MD5</c>, which RGW then rejects with a 403 on every upload.
/// </para>
/// </summary>
public sealed class StreamingRgwProxy : IRgwProxy
{
    private readonly ControlConfig _config;
    private readonly IHttpClientFactory _httpClientFactory;

    public StreamingRgwProxy(ControlConfig config, IHttpClientFactory httpClientFactory)
    {
        _config = config;
        _httpClientFactory = httpClientFactory;
    }

    public async Task ProxyAsync(HttpContext context)
    {
        var client = _httpClientFactory.CreateClient("rgw");
        var target = new Uri(_config.RgwEndpoint, context.Request.Path + context.Request.QueryString);
        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), target);

        if (HasBody(context.Request))
        {
            request.Content = new StreamContent(context.Request.Body);

            // Preserve the inbound framing. Without a length HttpClient falls back to chunked
            // transfer-encoding, which changes the wire format the signature was computed over and
            // which RGW can reject outright. Kestrel has already enforced that the body matches
            // this length, so forwarding it cannot contradict what is actually sent.
            if (context.Request.ContentLength is { } length)
                request.Content.Headers.ContentLength = length;
        }

        CopyRequestHeaders(context.Request, request);

        using var response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);

        context.Response.StatusCode = (int)response.StatusCode;
        foreach (var header in response.Headers)
            context.Response.Headers[header.Key] = header.Value.ToArray();
        foreach (var header in response.Content.Headers)
            context.Response.Headers[header.Key] = header.Value.ToArray();
        // Kestrel frames the response itself; passing RGW's framing through corrupts it.
        context.Response.Headers.Remove("transfer-encoding");
        await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
    }

    internal static void CopyRequestHeaders(HttpRequest source, HttpRequestMessage target)
    {
        foreach (var header in source.Headers)
        {
            if (!IsForwarded(header.Key))
                continue;

            var values = header.Value.ToArray();

            // Expect is a request property on HttpClient, not a header it will carry.
            if (header.Key.Equals("Expect", StringComparison.OrdinalIgnoreCase))
            {
                if (values.Any(value => value?.Contains("100-continue", StringComparison.OrdinalIgnoreCase) == true))
                    target.Headers.ExpectContinue = true;
                continue;
            }

            // Content headers are rejected by HttpRequestMessage.Headers and have to go on the
            // content. Hop-by-hop framing headers are dropped: HttpClient sets its own.
            if (IsContentHeader(header.Key))
            {
                if (target.Content is null || IsFraming(header.Key))
                    continue;
                target.Content.Headers.TryAddWithoutValidation(header.Key, values);
                continue;
            }

            target.Headers.TryAddWithoutValidation(header.Key, values);
        }
    }

    private static bool IsForwarded(string name)
        => name.StartsWith("x-amz-", StringComparison.OrdinalIgnoreCase)
           || name.Equals("Host", StringComparison.OrdinalIgnoreCase)
           || name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
           || name.Equals("Expect", StringComparison.OrdinalIgnoreCase)
           || name.Equals("Range", StringComparison.OrdinalIgnoreCase)
           || name.Equals("If-Match", StringComparison.OrdinalIgnoreCase)
           || name.Equals("If-None-Match", StringComparison.OrdinalIgnoreCase)
           || name.Equals("If-Modified-Since", StringComparison.OrdinalIgnoreCase)
           || name.Equals("If-Unmodified-Since", StringComparison.OrdinalIgnoreCase)
           || name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase);

    private static bool IsContentHeader(string name)
        => name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <c>Content-Length</c> is set from <c>HttpRequest.ContentLength</c> when the body is attached,
    /// so it is skipped here rather than added twice.
    /// </summary>
    private static bool IsFraming(string name)
        => name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase);

    private static bool HasBody(HttpRequest request)
        => request.Method is "PUT" or "POST" or "PATCH" or "DELETE";
}

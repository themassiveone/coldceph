using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.S3.Interfaces;

namespace ColdCeph.Control.Features.S3.Providers;

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
        CopyHeaders(context.Request, request);
        if (HasBody(context.Request.Method))
        {
            request.Content = new StreamContent(context.Request.Body);
            if (context.Request.ContentType is not null)
                request.Content.Headers.TryAddWithoutValidation("Content-Type", context.Request.ContentType);
        }

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
        context.Response.StatusCode = (int)response.StatusCode;
        foreach (var header in response.Headers)
            context.Response.Headers[header.Key] = header.Value.ToArray();
        if (response.Content is not null)
        {
            foreach (var header in response.Content.Headers)
                context.Response.Headers[header.Key] = header.Value.ToArray();
            context.Response.Headers.Remove("transfer-encoding");
            await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
    }

    private static void CopyHeaders(HttpRequest source, HttpRequestMessage target)
    {
        foreach (var header in source.Headers)
        {
            if (header.Key.StartsWith("x-amz-", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                || header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("Expect", StringComparison.OrdinalIgnoreCase))
            {
                if (!target.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()) && target.Content is not null)
                    target.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }
    }

    private static bool HasBody(string method)
        => method is "PUT" or "POST" or "DELETE" or "PATCH";
}

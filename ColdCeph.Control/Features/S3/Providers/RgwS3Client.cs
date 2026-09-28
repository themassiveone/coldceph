using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.S3.Interfaces;
using ColdCeph.Core.Features.S3.DTOs;

namespace ColdCeph.Control.Features.S3.Providers;

public sealed class RgwS3Client : IRgwObjectStore
{
    private readonly IHttpClientFactory _factory;
    private readonly ControlConfig _config;

    public RgwS3Client(IHttpClientFactory factory, ControlConfig config)
    {
        _factory = factory;
        _config = config;
    }

    public IReadOnlyList<S3BucketDto> ListBuckets()
        => S3ListParser.ParseBuckets(Send("GET", "/", query: null, body: null));

    public S3ObjectListingDto ListObjects(string bucket, string prefix)
    {
        var query = $"delimiter=%2F&list-type=2&prefix={Uri.EscapeDataString(prefix)}";
        var xml = Send("GET", $"/{Uri.EscapeDataString(bucket)}", query, null);
        return S3ListParser.ParseObjects(bucket, prefix, xml);
    }

    public Stream OpenObject(string bucket, string key)
    {
        var bytes = SendBytes("GET", ObjectPath(bucket, key), query: null, body: null);
        return new MemoryStream(bytes, writable: false);
    }

    public void PutObject(string bucket, string key, Stream body, string contentType)
    {
        using var copy = new MemoryStream();
        body.CopyTo(copy);
        SendBytes("PUT", ObjectPath(bucket, key), query: null, body: copy.ToArray(), contentType);
    }

    private static string ObjectPath(string bucket, string key)
        => "/" + Uri.EscapeDataString(bucket) + "/" + string.Join('/', key.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));

    private string Send(string method, string path, string? query, byte[]? body)
        => Encoding.UTF8.GetString(SendBytes(method, path, query, body));

    private byte[] SendBytes(string method, string path, string? query, byte[]? body, string? contentType = null)
    {
        var client = _factory.CreateClient("rgw");
        var builder = new UriBuilder(_config.RgwEndpoint)
        {
            Path = path,
            Query = query ?? string.Empty
        };
        using var request = new HttpRequestMessage(new HttpMethod(method), builder.Uri);
        if (body is not null)
            request.Content = new ByteArrayContent(body);
        if (!string.IsNullOrWhiteSpace(contentType) && request.Content is not null)
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        Sign(request, path, query ?? string.Empty, body);
        using var response = client.Send(request);
        var bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"RGW {method} {path} failed: {(int)response.StatusCode}");
        return bytes;
    }

    private void Sign(HttpRequestMessage request, string path, string query, byte[]? body)
    {
        var now = DateTime.UtcNow;
        var amzDate = now.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var dateStamp = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        const string payloadHash = "UNSIGNED-PAYLOAD";
        var host = request.RequestUri!.IsDefaultPort
            ? request.RequestUri.Host
            : $"{request.RequestUri.Host}:{request.RequestUri.Port}";
        request.Headers.Host = host;
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        var canonicalHeaders = $"host:{host}\nx-amz-content-sha256:{payloadHash}\nx-amz-date:{amzDate}\n";
        const string signedHeaders = "host;x-amz-content-sha256;x-amz-date";
        var canonicalQuery = string.Join('&', (query.StartsWith('?') ? query[1..] : query)
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(part => part, StringComparer.Ordinal));
        var canonicalRequest = $"{request.Method.Method}\n{path}\n{canonicalQuery}\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";
        var scope = $"{dateStamp}/{_config.S3Region}/s3/aws4_request";
        var stringToSign = $"AWS4-HMAC-SHA256\n{amzDate}\n{scope}\n{Sha256Hex(canonicalRequest)}";
        var signingKey = SigningKey(_config.S3SecretKey, dateStamp, _config.S3Region);
        var signature = Convert.ToHexString(Hmac(signingKey, stringToSign)).ToLowerInvariant();
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            $"AWS4-HMAC-SHA256 Credential={_config.S3AccessKey}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}");
        _ = body;
    }

    private static byte[] SigningKey(string secret, string dateStamp, string region)
    {
        var kDate = Hmac(Encoding.UTF8.GetBytes("AWS4" + secret), dateStamp);
        var kRegion = Hmac(kDate, region);
        var kService = Hmac(kRegion, "s3");
        return Hmac(kService, "aws4_request");
    }

    private static byte[] Hmac(byte[] key, string data)
        => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));

    private static string Sha256Hex(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

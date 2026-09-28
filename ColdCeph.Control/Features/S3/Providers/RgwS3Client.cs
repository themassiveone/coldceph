using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.S3.Interfaces;
using ColdCeph.Control.Shared;
using ColdCeph.Core.Features.S3.DTOs;

namespace ColdCeph.Control.Features.S3.Providers;

/// <summary>
/// The operator Buckets viewer's RGW client, signing its own SigV4 requests.
/// <para>
/// The signature covers the method, the canonical path, the canonical query and three headers, so
/// changing any of them has to change the signature. The clock is injected because the timestamp
/// is one of those inputs: without it the signature cannot be pinned, and a signer that ignored
/// its inputs would look the same as one that did not.
/// </para>
/// </summary>
public sealed class RgwS3Client : IRgwObjectStore
{
    private readonly IHttpClientFactory _factory;
    private readonly ControlConfig _config;
    private readonly IClock _clock;

    public RgwS3Client(IHttpClientFactory factory, ControlConfig config)
        : this(factory, config, new SystemClock())
    {
    }

    public RgwS3Client(IHttpClientFactory factory, ControlConfig config, IClock clock)
    {
        _factory = factory;
        _config = config;
        _clock = clock;
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

    /// <summary>
    /// Builds the target URI without letting it re-canonicalise the path.
    /// <para>
    /// The canonical path is part of the signature, so the bytes on the wire have to be the bytes
    /// that were signed. <c>UriBuilder</c> and the ordinary <c>Uri</c> constructor both re-process
    /// percent-escapes, which can leave the request path and the signed path disagreeing — and the
    /// only symptom is a 403 from RGW with nothing else to go on.
    /// </para>
    /// </summary>
    internal Uri Target(string path, string? query)
    {
        var authority = _config.RgwEndpoint.GetLeftPart(UriPartial.Authority);
        var suffix = string.IsNullOrEmpty(query) ? string.Empty : "?" + query;
        return new Uri(authority + path + suffix, new UriCreationOptions
        {
            DangerousDisablePathAndQueryCanonicalization = true
        });
    }

    private string Send(string method, string path, string? query, byte[]? body)
        => Encoding.UTF8.GetString(SendBytes(method, path, query, body));

    private byte[] SendBytes(string method, string path, string? query, byte[]? body, string? contentType = null)
    {
        var client = _factory.CreateClient("rgw");
        using var request = new HttpRequestMessage(new HttpMethod(method), Target(path, query));
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
        var now = _clock.UtcNow.UtcDateTime;
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

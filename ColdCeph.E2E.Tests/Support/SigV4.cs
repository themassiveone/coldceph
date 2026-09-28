using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ColdCeph.E2E.Tests.Support;

/// <summary>
/// Signs a request the way a real S3 client does, for the E2E journeys.
/// <para>
/// It signs the <em>payload hash</em> and includes <c>content-md5</c> and <c>content-type</c> in
/// the signed headers, rather than taking the <c>UNSIGNED-PAYLOAD</c> shortcut. That is
/// deliberate: it is what makes a dropped <c>Content-*</c> header show up as RGW's 403 instead of
/// passing unnoticed, which is precisely the proxy bug no test used to catch.
/// </para>
/// </summary>
internal static class SigV4
{
    public static void Sign(
        HttpRequestMessage request,
        byte[] payload,
        string accessKey,
        string secretKey,
        string region,
        DateTimeOffset? at = null)
    {
        var now = (at ?? DateTimeOffset.UtcNow).UtcDateTime;
        var amzDate = now.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var dateStamp = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var uri = request.RequestUri ?? throw new InvalidOperationException("A signed request needs a URI.");
        var host = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        var payloadHash = Hex(SHA256.HashData(payload));

        request.Headers.Host = host;
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);

        var headers = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["host"] = host,
            ["x-amz-content-sha256"] = payloadHash,
            ["x-amz-date"] = amzDate
        };

        if (request.Content is not null)
        {
            var md5 = Convert.ToBase64String(MD5.HashData(payload));
            request.Content.Headers.ContentMD5 = Convert.FromBase64String(md5);
            headers["content-md5"] = md5;
            if (request.Content.Headers.ContentType is { } contentType)
                headers["content-type"] = contentType.ToString();
        }

        var signedHeaders = string.Join(';', headers.Keys);
        var canonicalHeaders = string.Concat(headers.Select(pair => $"{pair.Key}:{pair.Value.Trim()}\n"));
        var canonicalQuery = CanonicalQuery(uri.Query);
        var canonicalRequest = string.Join('\n',
            request.Method.Method,
            uri.AbsolutePath,
            canonicalQuery,
            canonicalHeaders,
            signedHeaders,
            payloadHash);

        var scope = $"{dateStamp}/{region}/s3/aws4_request";
        var stringToSign = string.Join('\n', "AWS4-HMAC-SHA256", amzDate, scope, Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest))));
        var signature = Hex(Hmac(SigningKey(secretKey, dateStamp, region), stringToSign));

        request.Headers.TryAddWithoutValidation(
            "Authorization",
            $"AWS4-HMAC-SHA256 Credential={accessKey}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}");
    }

    private static string CanonicalQuery(string query)
    {
        var trimmed = query.StartsWith('?') ? query[1..] : query;
        if (string.IsNullOrEmpty(trimmed))
            return string.Empty;

        return string.Join('&', trimmed
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part =>
            {
                var separator = part.IndexOf('=', StringComparison.Ordinal);
                var name = separator < 0 ? part : part[..separator];
                var value = separator < 0 ? string.Empty : part[(separator + 1)..];
                return (Name: name, Value: value);
            })
            .OrderBy(pair => pair.Name, StringComparer.Ordinal)
            .ThenBy(pair => pair.Value, StringComparer.Ordinal)
            .Select(pair => $"{pair.Name}={pair.Value}"));
    }

    private static byte[] SigningKey(string secret, string dateStamp, string region)
    {
        var date = Hmac(Encoding.UTF8.GetBytes("AWS4" + secret), dateStamp);
        var scoped = Hmac(date, region);
        var service = Hmac(scoped, "s3");
        return Hmac(service, "aws4_request");
    }

    private static byte[] Hmac(byte[] key, string data)
        => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}

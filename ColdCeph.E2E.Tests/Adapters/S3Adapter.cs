using System.Net;
using System.Text;
using ColdCeph.E2E.Tests.States;
using ColdCeph.E2E.Tests.Support;
using Xcepto.Adapters;

namespace ColdCeph.E2E.Tests.Adapters;

/// <summary>
/// The cold S3 endpoint.
/// <para>
/// The old "is forwarded to RGW" step asserted only that the status was not 503, so a 404, a 403
/// from a broken signature, a 400 or a 500 all passed — and no test anywhere read or wrote an
/// object through ColdCeph. These assert statuses explicitly, and the round-trip proves the proxy
/// carries a body and its signed headers.
/// </para>
/// </summary>
public sealed class S3Adapter : XceptoAdapter
{
    private readonly HttpClient _client;
    private readonly Uri _baseUrl;
    private readonly string _bucket;
    private readonly string _accessKey;
    private readonly string _secretKey;
    private readonly string _region;

    internal S3Adapter(HttpClient client, Uri baseUrl, string bucket, string accessKey, string secretKey, string region)
    {
        _client = client;
        _baseUrl = baseUrl;
        _bucket = bucket;
        _accessKey = accessKey;
        _secretKey = secretKey;
        _region = region;
    }

    /// <summary>A cold plane refuses with 503 and a Retry-After, before reaching Ceph at all.</summary>
    public void SeeUnavailable()
    {
        AddStep(new ExpectationStepState("S3 refuses with 503 and Retry-After while cold", async () =>
        {
            var response = await Send(HttpMethod.Get, $"/{_bucket}/missing");
            return response.StatusCode == HttpStatusCode.ServiceUnavailable
                   && response.Headers.RetryAfter is not null;
        }));
    }

    /// <summary>
    /// A missing object on a ready plane is a 404 from RGW — which proves the request reached RGW
    /// and was accepted, rather than merely not being refused by ColdCeph.
    /// </summary>
    public void SeeMissingObjectIsNotFound()
    {
        AddStep(new ExpectationStepState("S3 GET of a missing object returns RGW's 404", async () =>
        {
            var response = await Send(HttpMethod.Get, $"/{_bucket}/definitely-not-here");
            if (response.StatusCode == HttpStatusCode.NotFound)
                return true;

            // 503 is ColdCeph refusing admission and is worth retrying; anything else is RGW
            // answering something unexpected, and the body says what.
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                return false;

            throw new InvalidOperationException(
                $"Expected RGW's 404 for a missing object but the cold endpoint answered "
                + $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }));
    }

    /// <summary>
    /// The journey no test used to make: write an object through the cold endpoint and read the
    /// same bytes back. A dropped signed header shows up here as RGW's 403, which is exactly the
    /// failure the proxy's header ordering used to cause on every upload.
    /// </summary>
    public void SeeObjectRoundTrip()
    {
        var key = $"e2e/round-trip-{Guid.NewGuid():N}.txt";
        var payload = $"coldceph round trip {Guid.NewGuid():N}";

        AddStep(new ActionStepState($"S3 PUT {_bucket}/{key}", async () =>
        {
            var response = await Send(HttpMethod.Put, $"/{_bucket}/{key}", payload);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"PUT through the cold endpoint failed: {(int)response.StatusCode} "
                    + $"{await response.Content.ReadAsStringAsync()}"
                    + (response.StatusCode == HttpStatusCode.ServiceUnavailable
                        ? ". A 503 is ColdCeph refusing admission, not RGW: the confirmation was not "
                          + "write-ready. SeeConfirmationAdmitsReadsAndWrites names the predicate."
                        : string.Empty));
        }));

        AddStep(new ExpectationStepState($"S3 GET {_bucket}/{key} returns the same bytes", async () =>
        {
            var response = await Send(HttpMethod.Get, $"/{_bucket}/{key}");
            if (!response.IsSuccessStatusCode)
                return false;
            return await response.Content.ReadAsStringAsync() == payload;
        }));
    }

    public void SeeBucketListing()
    {
        AddStep(new ExpectationStepState("S3 lists the bucket through the cold endpoint", async () =>
        {
            var response = await Send(HttpMethod.Get, $"/{_bucket}?list-type=2");
            if (!response.IsSuccessStatusCode)
                return false;
            var body = await response.Content.ReadAsStringAsync();
            return body.Contains("ListBucketResult", StringComparison.Ordinal);
        }));
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string pathAndQuery, string? payload = null)
    {
        var target = new Uri(_baseUrl, pathAndQuery);
        using var request = new HttpRequestMessage(method, target);
        var body = payload is null ? [] : Encoding.UTF8.GetBytes(payload);
        if (payload is not null)
            request.Content = new ByteArrayContent(body) { Headers = { ContentType = new("text/plain") } };
        SigV4.Sign(request, body, _accessKey, _secretKey, _region);
        return await _client.SendAsync(request);
    }
}

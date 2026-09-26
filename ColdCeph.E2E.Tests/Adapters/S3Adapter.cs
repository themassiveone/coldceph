using System.Net;
using ColdCeph.E2E.Tests.States;
using Xcepto.Adapters;

namespace ColdCeph.E2E.Tests.Adapters;

public sealed class S3Adapter : XceptoAdapter
{
    private readonly HttpClient _client;
    private readonly Uri _baseUrl;

    internal S3Adapter(HttpClient client, Uri baseUrl)
    {
        _client = client;
        _baseUrl = baseUrl;
    }

    public void GetObject(string bucket, string key)
    {
        AddStep(new ActionStepState($"S3 GET {bucket}/{key}", async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUrl, $"/{bucket}/{key}"));
            request.Headers.TryAddWithoutValidation("Host", _baseUrl.Authority);
            _ = await _client.SendAsync(request);
        }));
    }

    public void SeeUnavailable()
    {
        AddStep(new ExpectationStepState("S3 is unavailable", async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUrl, "/cold/missing"));
            request.Headers.TryAddWithoutValidation("Host", _baseUrl.Authority);
            var response = await _client.SendAsync(request);
            return response.StatusCode == HttpStatusCode.ServiceUnavailable
                   && response.Headers.RetryAfter is not null;
        }));
    }

    public void SeeForwarded()
    {
        AddStep(new ExpectationStepState("S3 is forwarded to RGW", async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUrl, "/cold/missing"));
            request.Headers.TryAddWithoutValidation("Host", _baseUrl.Authority);
            var response = await _client.SendAsync(request);
            return response.StatusCode is not HttpStatusCode.ServiceUnavailable;
        }));
    }
}

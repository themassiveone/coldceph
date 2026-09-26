using System.Net;
using ColdCeph.E2E.Tests.States;
using Xcepto.Adapters;

namespace ColdCeph.E2E.Tests.Adapters;

public sealed class AgentAdapter : XceptoAdapter
{
    private readonly HttpClient _client;
    private readonly Uri _baseUrl;
    private readonly string _token;

    internal AgentAdapter(HttpClient client, Uri baseUrl, string token)
    {
        _client = client;
        _baseUrl = baseUrl;
        _token = token;
    }

    public void SeeUnauthorizedWithoutToken()
    {
        AddStep(new ExpectationStepState("agent status requires token", async () =>
        {
            using var anonymous = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var response = await anonymous.GetAsync(new Uri(_baseUrl, "/v1/status"));
            return response.StatusCode == HttpStatusCode.Unauthorized;
        }));
    }

    public void SeeHost(string hostId)
    {
        AddStep(new ExpectationStepState($"agent host is {hostId}", async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUrl, "/v1/status"));
            request.Headers.Add("X-ColdCeph-Token", _token);
            var response = await _client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            return response.IsSuccessStatusCode && body.Contains(hostId, StringComparison.Ordinal);
        }));
    }

    public void SeeOsds()
    {
        AddStep(new ExpectationStepState("agent lists OSDs", async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUrl, "/v1/osds"));
            request.Headers.Add("X-ColdCeph-Token", _token);
            var response = await _client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            return response.IsSuccessStatusCode && body.Contains("osdId", StringComparison.OrdinalIgnoreCase);
        }));
    }
}

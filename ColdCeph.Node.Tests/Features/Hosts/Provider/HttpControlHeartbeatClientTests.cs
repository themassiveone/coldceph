using System.Net;
using ColdCeph.Node.Composition;
using ColdCeph.Node.Features.Hosts.Providers;
using ColdCeph.Core.Features.Hosts.DTOs;

namespace ColdCeph.Node.Tests.Features.Hosts.Provider;

[TestFixture]
public sealed class HttpControlHeartbeatClientTests
{
    [Test]
    public void Send_posts_a_join_request_without_a_token()
    {
        var handler = new RecordingHandler { Response = new HttpResponseMessage(HttpStatusCode.Accepted) };
        var client = new HttpControlHeartbeatClient(
            new FixedFactory(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8080") }),
            new NodeConfig
            {
                ControlEndpoint = new Uri("http://127.0.0.1:8080"),
                NodeToken = "should-not-be-sent"
            });

        client.Send(
            new NodeStatusDto { HostId = "dev", Hostname = "dev", ObservedAt = DateTimeOffset.UtcNow },
            new Uri("http://127.0.0.1:7080"));

        Assert.That(handler.Last!.RequestUri!.AbsolutePath, Is.EqualTo("/v1/hosts/join"));
        Assert.That(handler.Last.Headers.Contains("X-ColdCeph-Token"), Is.False);
        Assert.That(string.Join(',', handler.Last.Headers.GetValues("X-ColdCeph-Node-Endpoint")), Does.Contain("127.0.0.1:7080"));
    }

    [Test]
    public void Send_does_not_contact_control_when_the_endpoint_is_unset()
    {
        var handler = new RecordingHandler();
        var client = new HttpControlHeartbeatClient(
            new FixedFactory(new HttpClient(handler)),
            new NodeConfig { NodeToken = "secret" });

        client.Send(
            new NodeStatusDto { HostId = "dev", Hostname = "dev", ObservedAt = DateTimeOffset.UtcNow },
            new Uri("http://127.0.0.1:7080"));

        Assert.That(handler.Last, Is.Null);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }
        public HttpResponseMessage Response { get; init; } = new(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            return Task.FromResult(Response);
        }
    }

    private sealed class FixedFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public FixedFactory(HttpClient client) => _client = client;

        public HttpClient CreateClient(string name) => _client;
    }
}

using System.Net;
using ColdCeph.Node.Composition;
using ColdCeph.Node.Features.Osds.Providers;
using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Node.Tests.Features.Osds.Provider;

[TestFixture]
public sealed class HttpControlOsdsReporterTests
{
    [Test]
    public void Send_posts_observed_osds_with_the_node_token()
    {
        var handler = new RecordingHandler { Response = new HttpResponseMessage(HttpStatusCode.OK) };
        var reporter = new HttpControlOsdsReporter(
            new FixedFactory(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8080") }),
            new NodeConfig
            {
                ControlEndpoint = new Uri("http://127.0.0.1:8080"),
                NodeToken = "secret"
            });

        reporter.Send(new HostOsdsObservationDto { HostId = "dev", Osds = [] });

        Assert.That(handler.Last!.RequestUri!.AbsolutePath, Is.EqualTo("/v1/osds/observed"));
        Assert.That(string.Join(',', handler.Last.Headers.GetValues("X-ColdCeph-Token")), Is.EqualTo("secret"));
    }

    [Test]
    public void Send_does_not_contact_control_when_the_endpoint_is_unset()
    {
        var handler = new RecordingHandler();
        var reporter = new HttpControlOsdsReporter(
            new FixedFactory(new HttpClient(handler)),
            new NodeConfig { NodeToken = "secret" });

        reporter.Send(new HostOsdsObservationDto { HostId = "dev", Osds = [] });

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

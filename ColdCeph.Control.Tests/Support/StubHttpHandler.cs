using System.Net;

namespace ColdCeph.Control.Tests.Support;

/// <summary>
/// Captures the request a provider actually put on the wire, and answers with whatever the test
/// wants back. This is the seam for testing an HTTP provider: the thing worth asserting is the
/// bytes it sends and how it reacts to a real answer, not that it called a method.
/// </summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _respond = respond;
    }

    public static StubHttpHandler Returning(HttpStatusCode status, string body = "", string contentType = "application/json")
        => new(_ => new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, contentType) });

    public static StubHttpHandler Json(string body) => Returning(HttpStatusCode.OK, body);

    public HttpRequestMessage? LastRequest { get; private set; }

    public string? LastBody { get; private set; }

    public List<HttpRequestMessage> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Capture(request);
        if (request.Content is not null)
            LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
        return _respond(request);
    }

    /// <summary>
    /// RgwS3Client sends synchronously, and HttpClient does not bridge to SendAsync for a custom
    /// handler — it throws NotSupportedException unless this is overridden too.
    /// </summary>
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Capture(request);
        if (request.Content is not null)
            LastBody = request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
        return _respond(request);
    }

    private void Capture(HttpRequestMessage request)
    {
        LastRequest = request;
        Requests.Add(request);
    }

    public IEnumerable<string> HeaderValues(string name)
    {
        if (LastRequest is null)
            return [];
        if (LastRequest.Headers.TryGetValues(name, out var values))
            return values;
        if (LastRequest.Content is not null && LastRequest.Content.Headers.TryGetValues(name, out var contentValues))
            return contentValues;
        return [];
    }

    public bool HasHeader(string name) => HeaderValues(name).Any();

    public IHttpClientFactory AsFactory() => new SingleClientFactory(this);

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public SingleClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
            => new(_handler, disposeHandler: false) { BaseAddress = new Uri("http://rgw.test") };
    }
}

using System.Net;
using Xcepto.Builder;
using Xcepto.Interfaces;

namespace ColdCeph.E2E.Tests.Adapters;

public sealed class OperatorAdapterBuilder : AbstractAdapterBuilder<OperatorAdapterBuilder, OperatorAdapter>
{
    private Uri? _baseUrl;
    private HttpClient? _client;

    public OperatorAdapterBuilder(IStateMachineBuilder stateMachineBuilder) : base(stateMachineBuilder)
    {
    }

    public OperatorAdapterBuilder WithBaseUrl(Uri baseUrl)
    {
        _baseUrl = baseUrl;
        return this;
    }

    public OperatorAdapterBuilder WithHttpClient(HttpClient client)
    {
        _client = client;
        return this;
    }

    public override OperatorAdapter Build()
    {
        var baseUrl = _baseUrl ?? throw new InvalidOperationException("Operator adapter requires a base URL.");
        var client = _client ?? CreateCookieClient();
        var adapter = new OperatorAdapter(client, baseUrl);
        StateMachineBuilder.RegisterAdapter(adapter);
        return adapter;
    }

    private static HttpClient CreateCookieClient()
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            AllowAutoRedirect = false
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }
}

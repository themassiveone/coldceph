using Xcepto.Builder;
using Xcepto.Interfaces;

namespace ColdCeph.E2E.Tests.Adapters;

public sealed class NodeAdapterBuilder : AbstractAdapterBuilder<NodeAdapterBuilder, NodeAdapter>
{
    private Uri? _baseUrl;
    private string _token = "changeme";

    public NodeAdapterBuilder(IStateMachineBuilder stateMachineBuilder) : base(stateMachineBuilder)
    {
    }

    public NodeAdapterBuilder WithBaseUrl(Uri baseUrl)
    {
        _baseUrl = baseUrl;
        return this;
    }

    public NodeAdapterBuilder WithToken(string token)
    {
        _token = token;
        return this;
    }

    public override NodeAdapter Build()
    {
        var baseUrl = _baseUrl ?? throw new InvalidOperationException("Node adapter requires a base URL.");
        var adapter = new NodeAdapter(new HttpClient { Timeout = TimeSpan.FromSeconds(15) }, baseUrl, _token);
        StateMachineBuilder.RegisterAdapter(adapter);
        return adapter;
    }
}

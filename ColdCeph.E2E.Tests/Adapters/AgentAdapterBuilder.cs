using Xcepto.Builder;
using Xcepto.Interfaces;

namespace ColdCeph.E2E.Tests.Adapters;

public sealed class AgentAdapterBuilder : AbstractAdapterBuilder<AgentAdapterBuilder, AgentAdapter>
{
    private Uri? _baseUrl;
    private string _token = "changeme";

    public AgentAdapterBuilder(IStateMachineBuilder stateMachineBuilder) : base(stateMachineBuilder)
    {
    }

    public AgentAdapterBuilder WithBaseUrl(Uri baseUrl)
    {
        _baseUrl = baseUrl;
        return this;
    }

    public AgentAdapterBuilder WithToken(string token)
    {
        _token = token;
        return this;
    }

    public override AgentAdapter Build()
    {
        var baseUrl = _baseUrl ?? throw new InvalidOperationException("Agent adapter requires a base URL.");
        var adapter = new AgentAdapter(new HttpClient { Timeout = TimeSpan.FromSeconds(15) }, baseUrl, _token);
        StateMachineBuilder.RegisterAdapter(adapter);
        return adapter;
    }
}

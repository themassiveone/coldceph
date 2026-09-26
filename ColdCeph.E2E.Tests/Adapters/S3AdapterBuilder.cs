using Xcepto.Builder;
using Xcepto.Interfaces;

namespace ColdCeph.E2E.Tests.Adapters;

public sealed class S3AdapterBuilder : AbstractAdapterBuilder<S3AdapterBuilder, S3Adapter>
{
    private Uri? _baseUrl;

    public S3AdapterBuilder(IStateMachineBuilder stateMachineBuilder) : base(stateMachineBuilder)
    {
    }

    public S3AdapterBuilder WithBaseUrl(Uri baseUrl)
    {
        _baseUrl = baseUrl;
        return this;
    }

    public override S3Adapter Build()
    {
        var baseUrl = _baseUrl ?? throw new InvalidOperationException("S3 adapter requires a base URL.");
        var adapter = new S3Adapter(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, baseUrl);
        StateMachineBuilder.RegisterAdapter(adapter);
        return adapter;
    }
}

using Xcepto.Builder;
using Xcepto.Interfaces;

namespace ColdCeph.E2E.Tests.Adapters;

public sealed class S3AdapterBuilder : AbstractAdapterBuilder<S3AdapterBuilder, S3Adapter>
{
    private Uri? _baseUrl;
    private string _bucket = "cold";
    private string _accessKey = "coldceph";
    private string _secretKey = "coldcephsecret";
    private string _region = "us-east-1";

    public S3AdapterBuilder(IStateMachineBuilder stateMachineBuilder) : base(stateMachineBuilder)
    {
    }

    public S3AdapterBuilder WithBaseUrl(Uri baseUrl)
    {
        _baseUrl = baseUrl;
        return this;
    }

    public S3AdapterBuilder WithBucket(string bucket)
    {
        _bucket = bucket;
        return this;
    }

    public S3AdapterBuilder WithCredentials(string accessKey, string secretKey, string region = "us-east-1")
    {
        _accessKey = accessKey;
        _secretKey = secretKey;
        _region = region;
        return this;
    }

    public override S3Adapter Build()
    {
        var baseUrl = _baseUrl ?? throw new InvalidOperationException("S3 adapter requires a base URL.");
        var adapter = new S3Adapter(
            new HttpClient { Timeout = TimeSpan.FromSeconds(30) },
            baseUrl,
            _bucket,
            _accessKey,
            _secretKey,
            _region);
        StateMachineBuilder.RegisterAdapter(adapter);
        return adapter;
    }
}

using ColdCeph.E2E.Tests.Adapters;
using Xcepto.Builder;

namespace ColdCeph.E2E.Tests.Extensions;

public static class TransitionBuilderExtensions
{
    public static OperatorAdapterBuilder OperatorAdapterBuilder(this TransitionBuilder builder)
        => new(builder);

    public static AgentAdapterBuilder AgentAdapterBuilder(this TransitionBuilder builder)
        => new(builder);

    public static S3AdapterBuilder S3AdapterBuilder(this TransitionBuilder builder)
        => new(builder);

    public static CephAdapterBuilder CephAdapterBuilder(this TransitionBuilder builder)
        => new(builder);
}

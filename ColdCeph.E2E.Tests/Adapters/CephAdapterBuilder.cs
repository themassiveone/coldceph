using ColdCeph.Control.Composition;
using Xcepto.Builder;
using Xcepto.Interfaces;

namespace ColdCeph.E2E.Tests.Adapters;

public sealed class CephAdapterBuilder : AbstractAdapterBuilder<CephAdapterBuilder, CephAdapter>
{
    private string? _containerId;

    public CephAdapterBuilder(IStateMachineBuilder stateMachineBuilder) : base(stateMachineBuilder)
    {
    }

    public CephAdapterBuilder WithContainer(string containerId)
    {
        _containerId = containerId;
        return this;
    }

    public override CephAdapter Build()
    {
        var adapter = new CephAdapter(new ControlConfig
        {
            CephContainer = _containerId ?? throw new InvalidOperationException("Ceph adapter requires a container.")
        });
        StateMachineBuilder.RegisterAdapter(adapter);
        return adapter;
    }
}

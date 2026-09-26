using Xcepto.Builder;
using Xcepto.Interfaces;

namespace ColdCeph.E2E.Tests.Adapters;

public sealed class CephAdapterBuilder : AbstractAdapterBuilder<CephAdapterBuilder, CephAdapter>
{
    private string? _binary;
    private string? _workingDirectory;

    public CephAdapterBuilder(IStateMachineBuilder stateMachineBuilder) : base(stateMachineBuilder)
    {
    }

    public CephAdapterBuilder WithBinary(string binary)
    {
        _binary = binary;
        return this;
    }

    public CephAdapterBuilder WithWorkingDirectory(string workingDirectory)
    {
        _workingDirectory = workingDirectory;
        return this;
    }

    public override CephAdapter Build()
    {
        var adapter = new CephAdapter(
            _binary ?? throw new InvalidOperationException("Ceph adapter requires a binary."),
            _workingDirectory ?? throw new InvalidOperationException("Ceph adapter requires a working directory."));
        StateMachineBuilder.RegisterAdapter(adapter);
        return adapter;
    }
}

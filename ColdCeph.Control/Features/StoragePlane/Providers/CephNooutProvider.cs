using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.StoragePlane.Interfaces;
using ColdCeph.Control.Shared;

namespace ColdCeph.Control.Features.StoragePlane.Providers;

public sealed class CephNooutProvider : INooutProvider
{
    private readonly ControlConfig _config;
    private readonly IProcessRunner _runner;

    public CephNooutProvider(ControlConfig config, IProcessRunner runner)
    {
        _config = config;
        _runner = runner;
    }

    public void SetGroupNoout(string scope)
        => Run("osd", "set-group", "noout", scope);

    public void UnsetGroupNoout(string scope)
        => Run("osd", "unset-group", "noout", scope);

    private void Run(params string[] command)
    {
        var invoke = _config.InvokeCeph(command);
        _runner.Run(invoke.FileName, invoke.Arguments);
    }
}

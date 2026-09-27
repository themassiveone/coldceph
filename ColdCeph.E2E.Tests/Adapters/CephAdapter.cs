using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Integrity.Providers;
using ColdCeph.Control.Features.StoragePlane.Providers;
using ColdCeph.Control.Shared;
using ColdCeph.E2E.Tests.States;
using ColdCeph.E2E.Tests.Support;
using Xcepto.Adapters;

namespace ColdCeph.E2E.Tests.Adapters;

public sealed class CephAdapter : XceptoAdapter
{
    private readonly CephCliQueryProvider _query;
    private readonly CephNooutProvider _noout;

    internal CephAdapter(ControlConfig config)
    {
        var runner = new SerialProcessRunner(new SystemProcessRunner());
        _query = new CephCliQueryProvider(config, runner);
        _noout = new CephNooutProvider(config, runner);
    }

    public void SeeQuorum()
    {
        AddStep(new ExpectationStepState("Control ceph quorum_status succeeds", () =>
            Task.FromResult(_query.GetQuorumAvailable())));
    }

    public void SeeHealthNotSilent()
    {
        AddStep(new ExpectationStepState("Control ceph health detail reports HEALTH_*", () =>
        {
            var health = _query.GetHealthDetail();
            return Task.FromResult(
                health.Status.StartsWith("HEALTH_", StringComparison.Ordinal)
                && !string.Equals(health.Status, "UNAVAILABLE", StringComparison.Ordinal));
        }));
    }

    public void SeePgStat()
    {
        AddStep(new ExpectationStepState("Control ceph pg stat succeeds", () =>
        {
            _ = _query.GetPgsClean();
            return Task.FromResult(true);
        }));
    }

    public void SeeScopedNooutRoundTrip()
    {
        AddStep(new ActionStepState("Control ceph set-group and unset-group noout", () =>
        {
            _noout.SetGroupNoout(CephCluster.NooutScope);
            _noout.UnsetGroupNoout(CephCluster.NooutScope);
            return Task.CompletedTask;
        }));
    }
}

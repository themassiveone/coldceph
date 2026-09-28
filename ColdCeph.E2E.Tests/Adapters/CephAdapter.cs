using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Integrity.Providers;
using ColdCeph.Control.Features.StoragePlane.Providers;
using ColdCeph.Control.Shared;
using ColdCeph.Core.Features.Integrity.DTOs;
using ColdCeph.E2E.Tests.States;
using ColdCeph.E2E.Tests.Support;
using Xcepto.Adapters;

namespace ColdCeph.E2E.Tests.Adapters;

public sealed class CephAdapter : XceptoAdapter
{
    private readonly CephCliQueryProvider _query;
    private readonly CephNooutProvider _noout;
    private CephObservation? _observation;

    internal CephAdapter(ControlConfig config)
    {
        var runner = new SerialProcessRunner(new SystemProcessRunner());
        _query = new CephCliQueryProvider(config, runner);
        _noout = new CephNooutProvider(config, runner);
    }

    /// <summary>
    /// One confirmation for the whole journey, which is also the semantics under test: a
    /// confirmation is one pass over the monitor. Taking a fresh one per step meant four
    /// <c>docker exec … ceph</c> invocations each, and the journey outran its budget before it
    /// could assert anything.
    /// </summary>
    private CephObservation Observation => _observation ??= _query.GetObservation();

    public void SeeQuorum()
    {
        AddStep(new ExpectationStepState("Control reads a named Ceph quorum", () =>
            Task.FromResult(Observation.QuorumAvailable)));
    }

    public void SeeHealthNotSilent()
    {
        AddStep(new ExpectationStepState("Control ceph health detail reports HEALTH_*", () =>
        {
            var health = Observation.Health;
            return Task.FromResult(
                health.Status.StartsWith("HEALTH_", StringComparison.Ordinal)
                && !string.Equals(health.Status, "UNAVAILABLE", StringComparison.Ordinal));
        }));
    }

    public void SeeOsdMembership()
    {
        AddStep(new ExpectationStepState("Control ceph osd dump lists OSDs", () =>
            Task.FromResult(_query.ListOsdMembership().Count > 0)));
    }

    /// <summary>
    /// The live cluster reports PGs, and every group parses into recognisable state tokens.
    /// An unparsed <c>pgs_by_state</c> would leave every PG predicate false and hold the
    /// plane closed, so this asserts the parse produced something rather than that it ran.
    /// </summary>
    public void SeePgStatesParsed()
    {
        AddStep(new ExpectationStepState("Control parses Ceph PG states", () =>
        {
            var states = Observation.PgStates;
            return Task.FromResult(
                states.Count > 0
                && states.All(state => state.Count > 0 && state.Tokens.Any()));
        }));
    }

    /// <summary>
    /// Everything ColdCeph decides from comes back in a recognisable shape. This is what keeps
    /// the committed fixture corpus honest: if Ceph's schema moves, this fails here rather
    /// than shipping a provider that silently reads nothing.
    /// </summary>
    public void SeeConfirmationShapeMatchesFixtures()
    {
        AddStep(new ExpectationStepState("Live Ceph output parses to the shape the fixtures model", () =>
        {
            var observation = Observation;
            var namesLookRight = observation.HealthChecks.All(check =>
                check.Name.Length > 0
                && check.Name.All(character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character) || character == '_'));
            var capacityLooksRight = observation.Capacity is { TotalBytes: > 0 } capacity
                                     && capacity.UsedBytes >= 0
                                     && capacity.AvailableBytes >= 0;
            var signals = CephSignals.From(observation);
            return Task.FromResult(
                namesLookRight
                && capacityLooksRight
                && observation.PgStates.Count > 0
                && observation.QuorumAvailable
                && !signals.DurabilityFailure);
        }));
    }

    public void SeeScopedNooutRoundTrip()
    {
        AddStep(new ActionStepState("Control ceph set-group and unset-group noout", () =>
        {
            _noout.SetGroupNoout(SharedEnvironment.NooutScope);
            _noout.UnsetGroupNoout(SharedEnvironment.NooutScope);
            return Task.CompletedTask;
        }));
    }
}

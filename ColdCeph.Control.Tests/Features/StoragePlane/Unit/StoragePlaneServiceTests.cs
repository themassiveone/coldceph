using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.StoragePlane.Services;
using ColdCeph.Control.Tests.Fake;
using ColdCeph.Core.Features.Operations.DTOs;
using ColdCeph.Core.Features.StoragePlane.DTOs;

namespace ColdCeph.Control.Tests.Features.StoragePlane.Unit;

[TestFixture]
public sealed class StoragePlaneServiceTests
{
    [Test]
    public void RequestWake_from_cold_enters_waking_with_a_lease()
    {
        var plane = Create(out var repo, out _);
        var operationId = OperationIdRules.Create().Value;

        var lease = plane.RequestWake(operationId, "operator");

        Assert.That(plane.GetState().State, Is.EqualTo(StoragePlaneState.Waking));
        Assert.That(lease.OperationId, Is.EqualTo(operationId));
        Assert.That(repo.Load().LeaseHolder, Is.EqualTo("operator"));
    }

    [Test]
    public void RequestWake_rejects_invalid_operation_id()
    {
        var plane = Create(out _, out _);

        Assert.That(() => plane.RequestWake("not-an-id", "operator"), Throws.TypeOf<FormatException>());
        Assert.That(plane.GetState().State, Is.EqualTo(StoragePlaneState.Cold));
    }

    [Test]
    public void Second_wake_joins_the_existing_lease()
    {
        var plane = Create(out _, out _);
        var first = OperationIdRules.Create().Value;
        plane.RequestWake(first, "s3-pending");

        var second = plane.RequestWake(OperationIdRules.Create().Value, "s3-pending");

        Assert.That(plane.GetState().State, Is.EqualTo(StoragePlaneState.Waking));
        Assert.That(second.OperationId, Is.EqualTo(first));
    }

    [Test]
    public void Cold_cannot_skip_to_ready()
    {
        var plane = Create(out _, out _);

        Assert.That(() => plane.EnterReady(OperationIdRules.Create().Value), Throws.InvalidOperationException);
        Assert.That(plane.GetState().State, Is.EqualTo(StoragePlaneState.Cold));
    }

    [Test]
    public void Sleep_is_illegal_from_cold()
    {
        var plane = Create(out _, out _);

        Assert.That(() => plane.RequestSleep(OperationIdRules.Create().Value, "operator"), Throws.InvalidOperationException);
    }

    [Test]
    public void EnterSleeping_records_controller_owned_noout_and_never_marks_out()
    {
        var plane = Create(out var repo, out var noout);
        var operationId = OperationIdRules.Create().Value;
        plane.RequestWake(operationId, "operator");
        plane.EnterReady(operationId);
        plane.RequestSleep(operationId, "operator");
        plane.EnterSleeping(operationId);

        Assert.That(noout.Commands, Is.EqualTo(new[] { "osd set-group noout hdd-osds" }));
        Assert.That(repo.Load().OwnedNoout.Select(item => item.Scope), Does.Contain("hdd-osds"));
        Assert.That(noout.Commands.Any(command => command.Contains(" out") || command.Contains("destroy") || command.Contains("purge")), Is.False);
    }

    [Test]
    public void RejectUnknownNooutClear_refuses_flags_the_controller_does_not_own()
    {
        var plane = Create(out _, out _);

        Assert.That(() => plane.RejectUnknownNooutClear("operator-maintenance"), Throws.InvalidOperationException);
    }

    [Test]
    public void Persisted_cold_is_not_trusted_until_reality_is_marked()
    {
        var plane = Create(out var repo, out _);
        Assert.That(plane.GetState().Trusted, Is.False);
        Assert.That(repo.Load().State, Is.EqualTo(StoragePlaneState.Cold));

        plane.MarkObserved(StoragePlaneState.Cold, "startup-reconcile");

        Assert.That(plane.GetState().Trusted, Is.True);
    }

    [Test]
    public void Unexpected_integrity_can_fault_from_cold()
    {
        var plane = Create(out _, out _);
        plane.MarkObserved(StoragePlaneState.Cold, "startup-reconcile");

        plane.EnterFaulted("unexpected-integrity");

        Assert.That(plane.GetState().State, Is.EqualTo(StoragePlaneState.Faulted));
    }

    private static StoragePlaneService Create(out MemoryStoragePlaneRepository repo, out RecordingNooutProvider noout)
    {
        repo = new MemoryStoragePlaneRepository();
        noout = new RecordingNooutProvider();
        return new StoragePlaneService(repo, noout, new FakeClock(), new ControlConfig { BindHttpListeners = false });
    }
}

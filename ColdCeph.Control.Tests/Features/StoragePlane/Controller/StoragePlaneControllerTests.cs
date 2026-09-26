using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.StoragePlane.Controllers;
using ColdCeph.Control.Features.StoragePlane.Services;
using ColdCeph.Control.Tests.Fake;
using ColdCeph.Core.Features.Operations.DTOs;
using ColdCeph.Core.Features.StoragePlane.DTOs;

namespace ColdCeph.Control.Tests.Features.StoragePlane.Controller;

[TestFixture]
public sealed class StoragePlaneControllerTests
{
    [Test]
    public void Siblings_can_read_state_and_lease_without_mutating()
    {
        var service = new StoragePlaneService(new MemoryStoragePlaneRepository(), new RecordingNooutProvider(), new FakeClock(), new ControlConfig());
        var controller = new StoragePlaneController(service);

        var state = controller.GetState();

        Assert.That(state.State, Is.EqualTo(StoragePlaneState.Cold));
        Assert.That(controller.GetLease(), Is.Null);
        Assert.That(state.Trusted, Is.False);
    }

    [Test]
    public void RequestWake_is_the_protocol_command_and_changes_state()
    {
        var service = new StoragePlaneService(new MemoryStoragePlaneRepository(), new RecordingNooutProvider(), new FakeClock(), new ControlConfig());
        var controller = new StoragePlaneController(service);

        controller.RequestWake(OperationIdRules.Create().Value, "operator");

        Assert.That(controller.GetState().State, Is.EqualTo(StoragePlaneState.Waking));
        Assert.That(controller.GetLease(), Is.Not.Null);
    }
}

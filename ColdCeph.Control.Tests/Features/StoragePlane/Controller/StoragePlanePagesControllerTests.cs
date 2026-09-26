using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Auth.Controllers;
using ColdCeph.Control.Features.Auth.Services;
using ColdCeph.Control.Features.StoragePlane.Controllers;
using ColdCeph.Control.Features.StoragePlane.Services;
using ColdCeph.Control.Features.StoragePlane.ViewModels;
using ColdCeph.Control.Tests.Fake;
using ColdCeph.Core.Features.Operations.DTOs;
using ColdCeph.Core.Features.StoragePlane.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ColdCeph.Control.Tests.Features.StoragePlane.Controller;

[TestFixture]
public sealed class StoragePlanePagesControllerTests
{
    [Test]
    public void Index_offers_wake_from_cold_and_not_sleep()
    {
        var model = Model();

        Assert.That(model.CanWake, Is.True);
        Assert.That(model.CanSleep, Is.False);
        Assert.That(model.Snapshot.State, Is.EqualTo(StoragePlaneState.Cold));
    }

    [Test]
    public void Index_does_not_offer_wake_or_sleep_while_waking()
    {
        var model = Model(plane => plane.RequestWake(OperationIdRules.Create().Value, "operator"));

        Assert.That(model.CanWake, Is.False);
        Assert.That(model.CanSleep, Is.False);
        Assert.That(model.Snapshot.State, Is.EqualTo(StoragePlaneState.Waking));
    }

    [Test]
    public void Index_offers_sleep_from_ready_and_not_wake()
    {
        var model = Model(plane =>
        {
            var operationId = OperationIdRules.Create().Value;
            plane.RequestWake(operationId, "operator");
            plane.EnterReady(operationId);
        });

        Assert.That(model.CanSleep, Is.True);
        Assert.That(model.CanWake, Is.False);
        Assert.That(model.Snapshot.State, Is.EqualTo(StoragePlaneState.Ready));
    }

    private static StoragePlanePageViewModel Model(Action<StoragePlaneService>? arrange = null)
    {
        var service = new StoragePlaneService(
            new MemoryStoragePlaneRepository(),
            new RecordingNooutProvider(),
            new FakeClock(),
            new ControlConfig { BindHttpListeners = false });
        arrange?.Invoke(service);
        var pages = new StoragePlanePagesController(
            new StoragePlaneController(service),
            new AuthController(new AuthService(new ControlConfig(), new HttpContextAccessor())));
        var result = (ViewResult)pages.Index();
        return (StoragePlanePageViewModel)result.Model!;
    }
}

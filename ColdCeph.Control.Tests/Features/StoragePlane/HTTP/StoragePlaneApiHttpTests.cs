using System.Net;
using System.Net.Http.Json;
using ColdCeph.Control.Features.StoragePlane.Services;
using ColdCeph.Control.Tests.Support;
using ColdCeph.Core.Features.Operations.DTOs;
using ColdCeph.Core.Features.StoragePlane.DTOs;
using Microsoft.Extensions.DependencyInjection;

namespace ColdCeph.Control.Tests.Features.StoragePlane.HTTP;

/// <summary>
/// StoragePlane's <c>/v1</c> surface, which had no coverage at all. Wake and sleep are the two
/// commands that move the whole appliance, and they are legal only from specific states.
/// </summary>
[TestFixture]
public sealed class StoragePlaneApiHttpTests
{
    [Test]
    public async Task Anonymous_v1_is_401_without_a_login_redirect()
    {
        using var factory = new ControlAppFactory();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var state = await client.GetAsync("/v1/cluster/state");
        var wake = await client.PostAsync("/v1/cluster/wake", null);
        var sleep = await client.PostAsync("/v1/cluster/sleep", null);

        Assert.That(state.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(wake.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(sleep.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(state.Headers.Location, Is.Null);
    }

    /// <summary>An anonymous rejection must not reach the monitor.</summary>
    [Test]
    public async Task Anonymous_v1_does_not_confirm_ceph()
    {
        using var factory = new ControlAppFactory();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        _ = await client.GetAsync("/v1/cluster/state");
        _ = await client.PostAsync("/v1/cluster/wake", null);

        Assert.That(factory.Ceph.ObservationCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task State_reports_the_plane_without_confirming_ceph()
    {
        using var factory = new ControlAppFactory();
        using var client = await OperatorClient.SignedIn(factory);

        var snapshot = await client.GetFromJsonAsync<StoragePlaneSnapshot>("/v1/cluster/state");

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot!.State, Is.EqualTo(StoragePlaneState.Cold));
        Assert.That(factory.Ceph.ObservationCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task Wake_from_cold_is_accepted_and_takes_a_lease()
    {
        using var factory = new ControlAppFactory();
        using var client = await OperatorClient.SignedIn(factory);

        var response = await client.PostAsync("/v1/cluster/wake", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        var state = await client.GetFromJsonAsync<StoragePlaneSnapshot>("/v1/cluster/state");
        Assert.That(state!.State, Is.EqualTo(StoragePlaneState.Waking));
        Assert.That(Plane(factory).GetLease(), Is.Not.Null);
    }

    /// <summary>
    /// Many callers asking to wake is one wake: the lease is held by the first, and the rest join
    /// it rather than each starting a transition.
    /// </summary>
    [Test]
    public async Task Repeated_wakes_join_one_transition()
    {
        using var factory = new ControlAppFactory();
        using var client = await OperatorClient.SignedIn(factory);

        _ = await client.PostAsync("/v1/cluster/wake", null);
        var operationId = Plane(factory).GetState().ActiveOperationId;
        _ = await client.PostAsync("/v1/cluster/wake", null);
        _ = await client.PostAsync("/v1/cluster/wake", null);

        Assert.That(Plane(factory).GetState().ActiveOperationId, Is.EqualTo(operationId));
        Assert.That(Plane(factory).GetState().State, Is.EqualTo(StoragePlaneState.Waking));
    }

    /// <summary>Sleep is legal only from READY, so from COLD it must be refused, not queued.</summary>
    [Test]
    public async Task Sleep_from_cold_is_refused()
    {
        using var factory = new ControlAppFactory();
        using var client = await OperatorClient.SignedIn(factory);

        var response = await client.PostAsync("/v1/cluster/sleep", null);

        Assert.That((int)response.StatusCode, Is.GreaterThanOrEqualTo(400));
        Assert.That(Plane(factory).GetState().State, Is.EqualTo(StoragePlaneState.Cold));
    }

    [Test]
    public async Task Sleep_from_ready_is_accepted()
    {
        using var factory = new ControlAppFactory();
        using var client = await OperatorClient.SignedIn(factory);
        var plane = Plane(factory);
        var operationId = OperationIdRules.Create().Value;
        plane.MarkObserved(StoragePlaneState.Cold, "startup-reconcile");
        plane.RequestWake(operationId, "operator");
        plane.EnterReady(operationId);

        var response = await client.PostAsync("/v1/cluster/sleep", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        Assert.That(plane.GetState().State, Is.EqualTo(StoragePlaneState.Quiescing));
    }

    [Test]
    public async Task Wake_from_waking_does_not_move_the_plane_backwards()
    {
        using var factory = new ControlAppFactory();
        using var client = await OperatorClient.SignedIn(factory);
        _ = await client.PostAsync("/v1/cluster/wake", null);

        _ = await client.PostAsync("/v1/cluster/wake", null);

        Assert.That(Plane(factory).GetState().State, Is.EqualTo(StoragePlaneState.Waking));
    }

    /// <summary>
    /// Neither command reaches Ceph. StoragePlane transitions read node-pushed state and the last
    /// confirmation; the monitor is only consulted by the four external requests AGENTS.md names.
    /// </summary>
    [Test]
    public async Task Neither_wake_nor_sleep_confirms_ceph()
    {
        using var factory = new ControlAppFactory();
        using var client = await OperatorClient.SignedIn(factory);

        _ = await client.PostAsync("/v1/cluster/wake", null);
        _ = await client.PostAsync("/v1/cluster/sleep", null);

        Assert.That(factory.Ceph.ObservationCalls, Is.EqualTo(0));
        Assert.That(factory.Ceph.MembershipCalls, Is.EqualTo(0));
    }

    private static StoragePlaneService Plane(ControlAppFactory factory)
        => factory.Services.GetRequiredService<StoragePlaneService>();
}

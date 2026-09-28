using System.Net;
using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Devices.Providers;
using ColdCeph.Control.Tests.Support;
using ColdCeph.Core.Features.Devices.DTOs;
using ColdCeph.Core.Features.Operations.DTOs;

namespace ColdCeph.Control.Tests.Features.Devices.Provider;

/// <summary>
/// Same fail-closed contract as the OSD client. Reporting back the requested power state let an
/// unreachable node take the plane to COLD with the platters still spinning.
/// </summary>
[TestFixture]
public sealed class HttpNodeDevicesClientTests
{
    [Test]
    public void A_successful_wake_reports_what_the_node_said()
    {
        var handler = StubHttpHandler.Json("""{"deviceId":"d0","powerState":1,"idempotentHit":false}""");
        var client = Create(handler);

        var result = client.Wake(Endpoint, Request("d0", DevicePowerState.Active));

        Assert.That(result.DeviceId, Is.EqualTo("d0"));
        Assert.That(result.PowerState, Is.EqualTo(DevicePowerState.Active));
    }

    /// <summary>A drive that is still spinning up must be reported as still in standby.</summary>
    [Test]
    public void A_wake_the_node_says_did_not_take_is_reported_as_standby()
    {
        // 2 is Standby; Unknown is 0, so this also pins the wire encoding.
        var handler = StubHttpHandler.Json("""{"deviceId":"d0","powerState":2,"idempotentHit":false}""");
        var client = Create(handler);

        var result = client.Wake(Endpoint, Request("d0", DevicePowerState.Active));

        Assert.That(result.PowerState, Is.EqualTo(DevicePowerState.Standby));
    }

    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.Conflict)]
    [TestCase(HttpStatusCode.InternalServerError)]
    public void A_non_success_answer_throws_rather_than_claiming_the_desired_state(HttpStatusCode status)
    {
        var client = Create(StubHttpHandler.Returning(status, "{}"));

        Assert.That(() => client.Standby(Endpoint, Request("d0", DevicePowerState.Standby)),
            Throws.InvalidOperationException.With.Message.Contains(((int)status).ToString()));
    }

    /// <summary>
    /// A node refusing standby because the mapped OSD is still running is the invariant working.
    /// Control must hear the refusal, not record the disk as parked.
    /// </summary>
    [Test]
    public void A_refused_standby_does_not_record_the_disk_as_parked()
    {
        var client = Create(StubHttpHandler.Returning(HttpStatusCode.Conflict, """{"error":"osd still running"}"""));

        Assert.That(() => client.Standby(Endpoint, Request("d0", DevicePowerState.Standby)),
            Throws.InvalidOperationException);
    }

    [TestCase("")]
    [TestCase("null")]
    public void An_empty_body_throws(string body)
    {
        var client = Create(StubHttpHandler.Returning(HttpStatusCode.OK, body));

        Assert.That(() => client.Wake(Endpoint, Request("d0", DevicePowerState.Active)), Throws.Exception);
    }

    [Test]
    public void Wake_and_standby_use_the_node_command_paths()
    {
        var handler = StubHttpHandler.Json("""{"deviceId":"d0","powerState":1,"idempotentHit":false}""");
        var client = Create(handler);

        _ = client.Wake(Endpoint, Request("d0", DevicePowerState.Active));
        Assert.That(handler.LastRequest!.RequestUri!.AbsolutePath, Is.EqualTo("/v1/devices/d0/wake"));

        _ = client.Standby(Endpoint, Request("d0", DevicePowerState.Standby));
        Assert.That(handler.LastRequest!.RequestUri!.AbsolutePath, Is.EqualTo("/v1/devices/d0/standby"));
    }

    /// <summary>
    /// A node that cannot tell must report Unknown, and Unknown is not standby: a disk whose state
    /// is unreadable must never satisfy "every device is in standby".
    /// </summary>
    [Test]
    public void An_unknown_power_state_is_carried_through_as_unknown()
    {
        var handler = StubHttpHandler.Json("""{"deviceId":"d0","powerState":0,"idempotentHit":false}""");
        var client = Create(handler);

        var result = client.Standby(Endpoint, Request("d0", DevicePowerState.Standby));

        Assert.That(result.PowerState, Is.EqualTo(DevicePowerState.Unknown));
        Assert.That(result.PowerState, Is.Not.EqualTo(DevicePowerState.Standby));
    }

    [Test]
    public void The_node_token_is_sent()
    {
        var handler = StubHttpHandler.Json("""{"deviceId":"d0","powerState":1,"idempotentHit":false}""");
        var client = Create(handler, token: "s3cret");

        _ = client.Wake(Endpoint, Request("d0", DevicePowerState.Active));

        Assert.That(handler.HeaderValues("X-ColdCeph-Token"), Does.Contain("s3cret"));
    }

    private static readonly Uri Endpoint = new("http://node-a.test:7080");

    private static HttpNodeDevicesClient Create(StubHttpHandler handler, string token = "changeme")
        => new(handler.AsFactory(), new ControlConfig { NodeToken = token });

    private static DeviceMutationRequest Request(string deviceId, DevicePowerState desired)
        => new()
        {
            DeviceId = deviceId,
            OperationId = OperationIdRules.Create().Value,
            ControllerIdentity = "coldceph-control",
            Deadline = DateTimeOffset.UnixEpoch.AddYears(60),
            DesiredPowerState = desired
        };
}

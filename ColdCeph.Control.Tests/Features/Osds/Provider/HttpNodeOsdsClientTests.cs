using System.Net;
using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Osds.Providers;
using ColdCeph.Control.Tests.Support;
using ColdCeph.Core.Features.Operations.DTOs;
using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Control.Tests.Features.Osds.Provider;

/// <summary>
/// The client had no tests, and it used to answer an unparseable response by reporting back the
/// state it had asked for. That made an unreachable node read as "the OSD is running", which takes
/// the plane to READY and admits S3 traffic against OSDs that never started.
/// </summary>
[TestFixture]
public sealed class HttpNodeOsdsClientTests
{
    [Test]
    public void A_successful_start_reports_what_the_node_said()
    {
        var handler = StubHttpHandler.Json("""{"osdId":4,"processRunning":true,"idempotentHit":false}""");
        var client = Create(handler);

        var result = client.Start(Endpoint, Request(4, running: true));

        Assert.That(result.OsdId, Is.EqualTo(4));
        Assert.That(result.ProcessRunning, Is.True);
    }

    /// <summary>
    /// The case the old fake could not express and the old client got wrong: the node answered,
    /// and the OSD is still not running. That is what the first tens of seconds of every real
    /// wake look like.
    /// </summary>
    [Test]
    public void A_start_the_node_says_did_not_take_is_reported_as_not_running()
    {
        var handler = StubHttpHandler.Json("""{"osdId":4,"processRunning":false,"idempotentHit":false}""");
        var client = Create(handler);

        var result = client.Start(Endpoint, Request(4, running: true));

        Assert.That(result.ProcessRunning, Is.False);
    }

    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.Forbidden)]
    [TestCase(HttpStatusCode.NotFound)]
    [TestCase(HttpStatusCode.InternalServerError)]
    [TestCase(HttpStatusCode.BadGateway)]
    public void A_non_success_answer_throws_rather_than_claiming_the_desired_state(HttpStatusCode status)
    {
        var client = Create(StubHttpHandler.Returning(status, "{}"));

        Assert.That(() => client.Start(Endpoint, Request(4, running: true)),
            Throws.InvalidOperationException.With.Message.Contains(((int)status).ToString()));
    }

    [TestCase("")]
    [TestCase("null")]
    public void An_empty_body_throws_rather_than_claiming_the_desired_state(string body)
    {
        var client = Create(StubHttpHandler.Returning(HttpStatusCode.OK, body));

        Assert.That(() => client.Start(Endpoint, Request(4, running: true)), Throws.Exception);
    }

    [Test]
    public void A_transport_failure_throws()
    {
        var handler = new StubHttpHandler(_ => throw new HttpRequestException("connection refused"));
        var client = Create(handler);

        Assert.That(() => client.Start(Endpoint, Request(4, running: true)), Throws.Exception);
    }

    [Test]
    public void A_stop_the_node_says_did_not_take_is_not_reported_as_stopped()
    {
        var handler = StubHttpHandler.Json("""{"osdId":4,"processRunning":true,"idempotentHit":false}""");
        var client = Create(handler);

        var result = client.Stop(Endpoint, Request(4, running: false));

        Assert.That(result.ProcessRunning, Is.True);
    }

    [Test]
    public void Start_and_stop_use_the_node_command_paths()
    {
        var handler = StubHttpHandler.Json("""{"osdId":7,"processRunning":true,"idempotentHit":false}""");
        var client = Create(handler);

        _ = client.Start(Endpoint, Request(7, running: true));
        Assert.That(handler.LastRequest!.RequestUri!.AbsolutePath, Is.EqualTo("/v1/osds/7/start"));

        _ = client.Stop(Endpoint, Request(7, running: false));
        Assert.That(handler.LastRequest!.RequestUri!.AbsolutePath, Is.EqualTo("/v1/osds/7/stop"));
    }

    [Test]
    public void The_node_token_is_sent()
    {
        var handler = StubHttpHandler.Json("""{"osdId":4,"processRunning":true,"idempotentHit":false}""");
        var client = Create(handler, token: "s3cret");

        _ = client.Start(Endpoint, Request(4, running: true));

        Assert.That(handler.HeaderValues("X-ColdCeph-Token"), Does.Contain("s3cret"));
    }

    [Test]
    public void The_operation_id_reaches_the_node()
    {
        var handler = StubHttpHandler.Json("""{"osdId":4,"processRunning":true,"idempotentHit":false}""");
        var client = Create(handler);
        var request = Request(4, running: true);

        _ = client.Start(Endpoint, request);

        Assert.That(handler.LastBody, Does.Contain(request.OperationId));
    }

    private static readonly Uri Endpoint = new("http://node-a.test:7080");

    private static HttpNodeOsdsClient Create(StubHttpHandler handler, string token = "changeme")
        => new(handler.AsFactory(), new ControlConfig { NodeToken = token });

    private static OsdMutationRequest Request(int osdId, bool running)
        => new()
        {
            OsdId = osdId,
            OperationId = OperationIdRules.Create().Value,
            ControllerIdentity = "coldceph-control",
            Deadline = DateTimeOffset.UnixEpoch.AddYears(60),
            DesiredRunning = running
        };
}

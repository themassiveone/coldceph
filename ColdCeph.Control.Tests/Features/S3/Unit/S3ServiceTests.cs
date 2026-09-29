using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Integrity.Controllers;
using ColdCeph.Control.Features.Integrity.Services;
using ColdCeph.Control.Features.S3.Repositories;
using ColdCeph.Control.Features.S3.Services;
using ColdCeph.Control.Features.StoragePlane.Controllers;
using ColdCeph.Control.Features.StoragePlane.Services;
using ColdCeph.Control.Tests.Fake;
using ColdCeph.Core.Features.Operations.DTOs;
using ColdCeph.Core.Features.S3.DTOs;
using Microsoft.AspNetCore.Http;

namespace ColdCeph.Control.Tests.Features.S3.Unit;

[TestFixture]
public sealed class S3ServiceTests
{
    [Test]
    public void Retry_mode_returns_503_while_cold_and_records_pending_work()
    {
        var (s3, plane, ledger, proxy) = Create(S3AdmissionMode.Retry);
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Response.Body = new MemoryStream();

        s3.HandleAsync(context).GetAwaiter().GetResult();

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
        Assert.That(context.Response.Headers.RetryAfter.ToString(), Is.EqualTo("30"));
        Assert.That(proxy.Calls, Is.EqualTo(0));
        Assert.That(plane.GetState().State, Is.EqualTo(ColdCeph.Core.Features.StoragePlane.DTOs.StoragePlaneState.Cold));
        Assert.That(_ceph!.ObservationCalls, Is.EqualTo(0));
    }

    [Test]
    public void Wait_mode_while_cold_does_not_query_ceph()
    {
        var (s3, _, _, proxy) = Create(S3AdmissionMode.Wait);
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Response.Body = new MemoryStream();
        context.RequestAborted = new CancellationToken(canceled: true);

        s3.HandleAsync(context).GetAwaiter().GetResult();

        Assert.That(proxy.Calls, Is.EqualTo(0));
        Assert.That(_ceph!.ObservationCalls, Is.EqualTo(0));
    }

    [Test]
    public void Ready_write_without_write_ready_fails_closed()
    {
        var (s3, plane, _, proxy) = Create(S3AdmissionMode.Wait);
        var operationId = OperationIdRules.Create().Value;
        plane.RequestWake(operationId, "operator");
        plane.EnterReady(operationId);
        var ceph = _ceph!;
        ceph.Seeing(Support.CephFixture.Recovering);
        var context = new DefaultHttpContext();
        context.Request.Method = "PUT";
        context.Response.Body = new MemoryStream();

        s3.HandleAsync(context).GetAwaiter().GetResult();

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
        Assert.That(proxy.Calls, Is.EqualTo(0));
        Assert.That(_ceph!.ObservationCalls, Is.EqualTo(1));
    }

    [Test]
    public void Ready_get_is_proxied()
    {
        var (s3, plane, _, proxy) = Create(S3AdmissionMode.Wait);
        var operationId = OperationIdRules.Create().Value;
        plane.RequestWake(operationId, "operator");
        plane.EnterReady(operationId);
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Response.Body = new MemoryStream();

        s3.HandleAsync(context).GetAwaiter().GetResult();

        Assert.That(proxy.Calls, Is.EqualTo(1));
        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(_ceph!.ObservationCalls, Is.EqualTo(1));
    }

    [Test]
    public void Ready_get_confirms_ceph_only_once()
    {
        var (s3, plane, _, _) = Create(S3AdmissionMode.Wait);
        var operationId = OperationIdRules.Create().Value;
        plane.RequestWake(operationId, "operator");
        plane.EnterReady(operationId);
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Response.Body = new MemoryStream();

        s3.HandleAsync(context).GetAwaiter().GetResult();
        var calls = _ceph!.ObservationCalls;
        context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Response.Body = new MemoryStream();
        s3.HandleAsync(context).GetAwaiter().GetResult();

        Assert.That(calls, Is.EqualTo(1));
        Assert.That(_ceph.ObservationCalls, Is.EqualTo(2));
    }

    private FakeCephQueryProvider? _ceph;

    private (S3Service S3, StoragePlaneService Plane, MemoryRequestLedger Ledger, FakeRgwProxy Proxy) Create(S3AdmissionMode mode)
    {
        var clock = new FakeClock();
        var config = new ControlConfig { S3Mode = mode, RetryAfterSeconds = 30 };
        var plane = new StoragePlaneService(new MemoryStoragePlaneRepository(), new RecordingNooutProvider(), clock, config);
        var planeController = new StoragePlaneController(plane);
        _ceph = new FakeCephQueryProvider();
        var integrity = new IntegrityController(new IntegrityService(_ceph, new MemoryIntegrityRepository(), planeController, clock));
        var ledger = new MemoryRequestLedger(clock);
        var proxy = new FakeRgwProxy();
        var s3 = new S3Service(ledger, planeController, integrity, proxy, config);
        return (s3, plane, ledger, proxy);
    }
}

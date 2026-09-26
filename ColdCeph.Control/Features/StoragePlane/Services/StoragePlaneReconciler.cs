using ColdCeph.Control.Features.Devices.Controllers;
using ColdCeph.Control.Features.Integrity.Controllers;
using ColdCeph.Control.Features.Osds.Controllers;
using ColdCeph.Control.Features.S3.Controllers;
using ColdCeph.Control.Features.StoragePlane.Services;
using ColdCeph.Core.Features.Integrity.DTOs;
using ColdCeph.Core.Features.Operations.DTOs;
using ColdCeph.Core.Features.StoragePlane.DTOs;
using Microsoft.Extensions.Hosting;

namespace ColdCeph.Control.Features.StoragePlane.Services;

public sealed class StoragePlaneReconciler : BackgroundService
{
    private readonly StoragePlaneService _plane;
    private readonly S3Controller _s3;
    private readonly IntegrityController _integrity;
    private readonly OsdsController _osds;
    private readonly Devices.Controllers.DevicesController _devices;

    public StoragePlaneReconciler(
        StoragePlaneService plane,
        S3Controller s3,
        IntegrityController integrity,
        OsdsController osds,
        DevicesController devices)
    {
        _plane = plane;
        _s3 = s3;
        _integrity = integrity;
        _osds = osds;
        _devices = devices;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            ReconcileOnce();
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    public void ReconcileOnce()
    {
        var snapshot = _plane.GetState();
        var integrity = _integrity.GetIntegrity();
        var pending = _s3.GetPendingWork();
        var operationId = snapshot.ActiveOperationId ?? OperationIdRules.Create().Value;

        if (HasUnexpectedIntegrity(integrity) && snapshot.State != StoragePlaneState.Faulted)
        {
            _plane.EnterFaulted("unexpected-integrity");
            return;
        }

        if (!snapshot.Trusted)
        {
            _plane.MarkObserved(InferReality(integrity), "startup-reconcile");
            snapshot = _plane.GetState();
        }

        if (pending.HasPendingWork && snapshot.State == StoragePlaneState.Cold)
            _plane.RequestWake(operationId, "s3-pending");

        if (snapshot.State == StoragePlaneState.Waking
            && _osds.IsEveryProcessRunning()
            && integrity.Predicates.ReadReady)
            _plane.EnterReady(operationId);

        if (snapshot.State == StoragePlaneState.Ready
            && !pending.HasPendingWork
            && pending.ActiveCount == 0
            && integrity.Predicates.SleepSafe
            && _plane.IsIdle(pending.LastActivity))
            _plane.RequestSleep(operationId, "idle-policy");

        if (snapshot.State == StoragePlaneState.Quiescing && pending.ActiveCount == 0 && !pending.HasPendingWork)
            _plane.EnterSleeping(operationId);

        if (snapshot.State == StoragePlaneState.Sleeping
            && _osds.IsEveryProcessStopped()
            && _devices.IsEveryDeviceStandby())
            _plane.EnterCold(operationId);
    }

    private static bool HasUnexpectedIntegrity(IntegritySnapshot integrity)
        => integrity.Checks.Any(check => check.Classification == HealthClassification.Unexpected);

    private StoragePlaneState InferReality(IntegritySnapshot integrity)
    {
        if (_osds.IsEveryProcessRunning() && integrity.Predicates.ReadReady)
            return StoragePlaneState.Ready;
        if (_osds.IsEveryProcessRunning() || !_devices.IsEveryDeviceStandby())
            return StoragePlaneState.Waking;
        return StoragePlaneState.Cold;
    }
}

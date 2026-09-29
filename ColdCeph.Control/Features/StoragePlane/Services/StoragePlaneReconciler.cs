using ColdCeph.Control.Features.Devices.Controllers;
using ColdCeph.Control.Features.Integrity.Controllers;
using ColdCeph.Control.Features.Osds.Controllers;
using ColdCeph.Control.Features.S3.Controllers;
using ColdCeph.Control.Shared;
using ColdCeph.Core.Features.Integrity.DTOs;
using ColdCeph.Core.Features.Operations.DTOs;
using ColdCeph.Core.Features.StoragePlane.DTOs;
using Microsoft.Extensions.Logging;

namespace ColdCeph.Control.Features.StoragePlane.Services;

public sealed class StoragePlaneReconciler : ReconcilerLoop
{
    private readonly StoragePlaneService _plane;
    private readonly S3Controller _s3;
    private readonly IntegrityController _integrity;
    private readonly OsdsController _osds;
    private readonly DevicesController _devices;

    public StoragePlaneReconciler(
        StoragePlaneService plane,
        S3Controller s3,
        IntegrityController integrity,
        OsdsController osds,
        DevicesController devices,
        ILogger<StoragePlaneReconciler> logger)
        : base(logger)
    {
        _plane = plane;
        _s3 = s3;
        _integrity = integrity;
        _osds = osds;
        _devices = devices;
    }

    protected override void ReconcileBody()
    {
        var integrity = _integrity.GetLastIntegrity();
        var pending = _s3.GetPendingWork();

        if (IntegrityDurability.HasFailure(integrity) && State() != StoragePlaneState.Faulted)
        {
            _plane.EnterFaulted("unexpected-integrity");
            return;
        }

        if (!_plane.GetState().Trusted)
            _plane.MarkObserved(InferReality(), "startup-reconcile");

        // Each guard reads the state as it is now. Deciding several transitions from one
        // stale snapshot made the sequence depend on which tick a change landed in.
        if (pending.HasPendingWork && State() == StoragePlaneState.Cold)
            _plane.RequestWake(OperationId(), "s3-pending");

        if (State() == StoragePlaneState.Waking && _osds.IsEveryProcessRunning())
            _plane.EnterReady(OperationId());

        if (State() == StoragePlaneState.Ready
            && !pending.HasPendingWork
            && pending.ActiveCount == 0
            && integrity.Predicates.SleepSafe
            && _plane.IsIdle(pending.LastActivity))
            _plane.RequestSleep(OperationId(), "idle-policy");

        if (State() == StoragePlaneState.Quiescing && pending.ActiveCount == 0 && !pending.HasPendingWork)
            _plane.EnterSleeping(OperationId());

        if (State() == StoragePlaneState.Sleeping
            && _osds.IsEveryProcessStopped()
            && _devices.IsEveryDeviceStandby())
            _plane.EnterCold(OperationId());
    }

    private StoragePlaneState State() => _plane.GetState().State;

    private string OperationId()
        => _plane.GetState().ActiveOperationId ?? OperationIdRules.Create().Value;

    private StoragePlaneState InferReality()
    {
        if (_osds.IsEveryProcessRunning())
            return StoragePlaneState.Ready;
        if (!_osds.IsEveryProcessStopped() || !_devices.IsEveryDeviceStandby())
            return StoragePlaneState.Waking;
        return StoragePlaneState.Cold;
    }
}

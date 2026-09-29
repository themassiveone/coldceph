using ColdCeph.Control.Features.Hosts.Controllers;
using ColdCeph.Control.Features.Osds.Controllers;
using ColdCeph.Control.Features.StoragePlane.Controllers;
using ColdCeph.Control.Shared;
using ColdCeph.Core.Features.Operations.DTOs;
using ColdCeph.Core.Features.StoragePlane.DTOs;
using Microsoft.Extensions.Logging;

namespace ColdCeph.Control.Features.Devices.Services;

public sealed class DevicesReconciler : ReconcilerLoop
{
    private readonly DevicesService _devices;
    private readonly StoragePlaneController _plane;
    private readonly OsdsController _osds;
    private readonly HostsController _hosts;

    public DevicesReconciler(
        DevicesService devices,
        StoragePlaneController plane,
        OsdsController osds,
        HostsController hosts,
        ILogger<DevicesReconciler> logger)
        : base(logger)
    {
        _devices = devices;
        _plane = plane;
        _osds = osds;
        _hosts = hosts;
    }

    protected override void ReconcileBody()
    {
        var snapshot = _plane.GetState();
        var state = snapshot.State;
        if (state is not (StoragePlaneState.Waking or StoragePlaneState.Sleeping))
            return;

        var operationId = snapshot.ActiveOperationId ?? OperationIdRules.Create().Value;

        // A disk never spins down while its OSD is still running.
        if (state == StoragePlaneState.Sleeping && !_osds.IsEveryProcessStopped())
            return;

        foreach (var host in _hosts.ListHosts())
        {
            try
            {
                if (state == StoragePlaneState.Waking)
                    _devices.WakeDrifted(host.HostId, host.Endpoint, operationId);
                else
                    _devices.StandbyDrifted(host.HostId, host.Endpoint, operationId);
            }
            catch (Exception exception)
            {
                // One down node must not skip the others, but it must still be visible.
                Record($"{nameof(DevicesReconciler)} could not reach host {host.HostId}", exception);
            }
        }
    }
}

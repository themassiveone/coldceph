using ColdCeph.Control.Features.Devices.Controllers;
using ColdCeph.Control.Features.Hosts.Controllers;
using ColdCeph.Control.Features.StoragePlane.Controllers;
using ColdCeph.Control.Shared;
using ColdCeph.Core.Features.Devices.DTOs;
using ColdCeph.Core.Features.Operations.DTOs;
using ColdCeph.Core.Features.Osds.DTOs;
using ColdCeph.Core.Features.StoragePlane.DTOs;
using Microsoft.Extensions.Logging;

namespace ColdCeph.Control.Features.Osds.Services;

public sealed class OsdsReconciler : ReconcilerLoop
{
    private readonly OsdsService _osds;
    private readonly StoragePlaneController _plane;
    private readonly HostsController _hosts;
    private readonly DevicesController _devices;

    public OsdsReconciler(
        OsdsService osds,
        StoragePlaneController plane,
        HostsController hosts,
        DevicesController devices,
        ILogger<OsdsReconciler> logger)
        : base(logger)
    {
        _osds = osds;
        _plane = plane;
        _hosts = hosts;
        _devices = devices;
    }

    protected override void ReconcileBody()
    {
        var snapshot = _plane.GetState();
        var state = snapshot.State;
        if (state is not (StoragePlaneState.Waking or StoragePlaneState.Sleeping))
            return;

        var operationId = snapshot.ActiveOperationId ?? OperationIdRules.Create().Value;
        var diskPower = _devices.ListDevices().ToDictionary(device => device.DeviceId, StringComparer.Ordinal);

        foreach (var host in _hosts.ListHosts())
        {
            try
            {
                if (state == StoragePlaneState.Waking)
                    _osds.StartDrifted(host.HostId, host.Endpoint, operationId, osd => MappedDiskIsAwake(osd, diskPower));
                else
                    _osds.StopDrifted(host.HostId, host.Endpoint, operationId);
            }
            catch (Exception exception)
            {
                // One down node must not skip the others, but it must still be visible.
                Record($"{nameof(OsdsReconciler)} could not reach host {host.HostId}", exception);
            }
        }
    }

    /// <summary>
    /// Never start an OSD on a disk that is still spun down. Devices owns disk power, so this
    /// reads its inventory and waits — it does not issue the wake itself.
    /// <para>
    /// An OSD whose device is unknown to Devices is allowed through: a node that reports OSDs but
    /// no devices (a container, a test runtime) would otherwise never reach READY.
    /// </para>
    /// </summary>
    private static bool MappedDiskIsAwake(OsdDto osd, IReadOnlyDictionary<string, DeviceDto> diskPower)
        => osd.DeviceId is not { Length: > 0 } deviceId
           || !diskPower.TryGetValue(deviceId, out var device)
           || device.PowerState == DevicePowerState.Active;
}

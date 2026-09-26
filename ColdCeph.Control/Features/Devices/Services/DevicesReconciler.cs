using ColdCeph.Control.Features.Devices.Services;
using ColdCeph.Control.Features.Hosts.Controllers;
using ColdCeph.Control.Features.Osds.Controllers;
using ColdCeph.Control.Features.StoragePlane.Controllers;
using ColdCeph.Core.Features.Operations.DTOs;
using ColdCeph.Core.Features.StoragePlane.DTOs;
using Microsoft.Extensions.Hosting;

namespace ColdCeph.Control.Features.Devices.Services;

public sealed class DevicesReconciler : BackgroundService
{
    private readonly DevicesService _devices;
    private readonly StoragePlaneController _plane;
    private readonly OsdsController _osds;
    private readonly HostsController _hosts;

    public DevicesReconciler(
        DevicesService devices,
        StoragePlaneController plane,
        OsdsController osds,
        HostsController hosts)
    {
        _devices = devices;
        _plane = plane;
        _osds = osds;
        _hosts = hosts;
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
        try
        {
            ReconcileBody();
        }
        catch (Exception)
        {
            // A failed agent call must not stop the hosted loop.
        }
    }

    private void ReconcileBody()
    {
        var state = _plane.GetState().State;
        var host = _hosts.ListHosts().FirstOrDefault();
        if (host is null)
            return;

        var operationId = _plane.GetState().ActiveOperationId ?? OperationIdRules.Create().Value;
        _devices.RefreshFromAgent(host.Endpoint);
        if (state == StoragePlaneState.Waking)
            _devices.WakeAll(host.Endpoint, operationId);
        if (state == StoragePlaneState.Sleeping && _osds.IsEveryProcessStopped())
            _devices.StandbyAll(host.Endpoint, operationId);
    }
}

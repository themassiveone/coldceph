using ColdCeph.Control.Features.Hosts.Controllers;
using ColdCeph.Control.Features.Osds.Services;
using ColdCeph.Control.Features.StoragePlane.Controllers;
using ColdCeph.Core.Features.Operations.DTOs;
using ColdCeph.Core.Features.StoragePlane.DTOs;
using Microsoft.Extensions.Hosting;

namespace ColdCeph.Control.Features.Osds.Services;

public sealed class OsdsReconciler : BackgroundService
{
    private readonly OsdsService _osds;
    private readonly StoragePlaneController _plane;
    private readonly HostsController _hosts;

    public OsdsReconciler(OsdsService osds, StoragePlaneController plane, HostsController hosts)
    {
        _osds = osds;
        _plane = plane;
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
        _osds.RefreshFromAgent(host.Endpoint);
        if (state == StoragePlaneState.Waking)
            _osds.StartAll(host.Endpoint, operationId);
        if (state == StoragePlaneState.Sleeping)
            _osds.StopAll(host.Endpoint, operationId);
    }
}

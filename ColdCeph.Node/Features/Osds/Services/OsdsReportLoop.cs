using ColdCeph.Node.Composition;
using ColdCeph.Node.Features.Hosts.Controllers;
using ColdCeph.Node.Features.Osds.Controllers;
using ColdCeph.Node.Features.Osds.Interfaces;
using ColdCeph.Core.Features.Osds.DTOs;
using Microsoft.Extensions.Hosting;

namespace ColdCeph.Node.Features.Osds.Services;

public sealed class OsdsReportLoop : BackgroundService
{
    private readonly OsdsController _osds;
    private readonly HostsController _hosts;
    private readonly IControlOsdsReporter _reporter;
    private readonly NodeConfig _config;
    private string? _lastFingerprint;

    public OsdsReportLoop(OsdsController osds, HostsController hosts, IControlOsdsReporter reporter, NodeConfig config)
    {
        _osds = osds;
        _hosts = hosts;
        _reporter = reporter;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            ReportOnce();
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    public void ReportOnce()
    {
        if (_config.ControlEndpoint is null || !_hosts.IsControlEnrolled())
            return;

        try
        {
            var osds = _osds.ListOsds();
            var error = _osds.GetLastDiscoveryError();
            var fingerprint = Fingerprint(osds, error);
            if (fingerprint == _lastFingerprint)
                return;
            _reporter.Send(new HostOsdsObservationDto
            {
                HostId = _config.HostId,
                Osds = osds,
                Error = error
            });
            _lastFingerprint = fingerprint;
        }
        catch (Exception)
        {
            // Control being down must not stop the node.
        }
    }

    private static string Fingerprint(IReadOnlyList<OsdDto> osds, string? error)
        => $"{error}|{string.Join('|', osds.OrderBy(osd => osd.OsdId).Select(osd => $"{osd.OsdId}:{osd.ProcessRunning}:{osd.DeviceId}"))}";
}

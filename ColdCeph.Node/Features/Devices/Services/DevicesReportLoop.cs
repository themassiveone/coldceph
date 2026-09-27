using ColdCeph.Node.Composition;
using ColdCeph.Node.Features.Devices.Controllers;
using ColdCeph.Node.Features.Devices.Interfaces;
using ColdCeph.Node.Features.Hosts.Controllers;
using ColdCeph.Core.Features.Devices.DTOs;
using Microsoft.Extensions.Hosting;

namespace ColdCeph.Node.Features.Devices.Services;

public sealed class DevicesReportLoop : BackgroundService
{
    private readonly DevicesController _devices;
    private readonly HostsController _hosts;
    private readonly IControlDevicesReporter _reporter;
    private readonly NodeConfig _config;
    private string? _lastFingerprint;

    public DevicesReportLoop(
        DevicesController devices,
        HostsController hosts,
        IControlDevicesReporter reporter,
        NodeConfig config)
    {
        _devices = devices;
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
            var devices = _devices.ListDevices();
            var fingerprint = Fingerprint(devices);
            if (fingerprint == _lastFingerprint)
                return;
            _reporter.Send(new HostDevicesObservationDto
            {
                HostId = _config.HostId,
                Devices = devices
            });
            _lastFingerprint = fingerprint;
        }
        catch (Exception)
        {
            // Control being down must not stop the node.
        }
    }

    private static string Fingerprint(IReadOnlyList<DeviceDto> devices)
        => string.Join('|', devices.OrderBy(device => device.DeviceId)
            .Select(device => $"{device.DeviceId}:{device.PowerState}:{device.MappedOsdId}"));
}

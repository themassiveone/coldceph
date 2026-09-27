using ColdCeph.Node.Composition;
using ColdCeph.Node.Features.Osds.Interfaces;
using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Node.Features.Osds.Services;

public sealed class OsdsService
{
    private readonly IOsdRuntime _runtime;
    private readonly NodeConfig _config;
    private readonly Dictionary<int, OsdDto> _osds = [];
    private readonly object _gate = new();
    private string? _lastDiscoveryError;

    public OsdsService(IOsdRuntime runtime, NodeConfig config)
    {
        _runtime = runtime;
        _config = config;
    }

    public IReadOnlyList<OsdDto> ListOsds()
    {
        lock (_gate)
        {
            SyncFromRuntime();
            return _osds.Values.Select(Refresh).ToArray();
        }
    }

    public string? GetLastDiscoveryError()
    {
        lock (_gate)
            return _lastDiscoveryError;
    }

    public OsdDto? GetOsd(int osdId)
    {
        lock (_gate)
        {
            SyncFromRuntime();
            return _osds.TryGetValue(osdId, out var osd) ? Refresh(osd) : null;
        }
    }

    public void Seed(OsdDto osd)
    {
        lock (_gate)
            _osds[osd.OsdId] = osd;
    }

    public OsdMutationResult Start(OsdMutationRequest request)
    {
        lock (_gate)
        {
            var current = Require(request.OsdId);
            if (current.ProcessRunning)
                return new OsdMutationResult(request.OsdId, true, true);
            _runtime.Start(request.OsdId);
            _osds[request.OsdId] = current with { ProcessRunning = true, Up = true };
            return new OsdMutationResult(request.OsdId, true, false);
        }
    }

    public OsdMutationResult Stop(OsdMutationRequest request)
    {
        lock (_gate)
        {
            var current = Require(request.OsdId);
            if (!current.ProcessRunning)
                return new OsdMutationResult(request.OsdId, false, true);
            _runtime.Stop(request.OsdId);
            _osds[request.OsdId] = current with { ProcessRunning = false, Up = false };
            return new OsdMutationResult(request.OsdId, false, false);
        }
    }

    private void SyncFromRuntime()
    {
        try
        {
            var ids = _runtime.ListIds();
            _lastDiscoveryError = null;
            foreach (var osdId in ids)
            {
                if (_osds.ContainsKey(osdId))
                    continue;
                _osds[osdId] = new OsdDto
                {
                    OsdId = osdId,
                    HostId = _config.HostId,
                    DeviceId = $"{_config.HostId}-osd-{osdId}",
                    Up = false,
                    In = true,
                    ProcessRunning = false
                };
            }

            foreach (var stale in _osds.Keys.Except(ids).ToArray())
                _osds.Remove(stale);
        }
        catch (InvalidOperationException exception)
        {
            _lastDiscoveryError = exception.Message;
        }
    }

    private OsdDto Refresh(OsdDto osd)
        => osd with { ProcessRunning = _runtime.IsRunning(osd.OsdId), HostId = _config.HostId };

    private OsdDto Require(int osdId)
    {
        SyncFromRuntime();
        return _osds.TryGetValue(osdId, out var osd)
            ? Refresh(osd)
            : throw new InvalidOperationException($"Unknown OSD {osdId}.");
    }
}

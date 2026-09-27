using ColdCeph.Node.Composition;
using ColdCeph.Node.Features.Osds.Interfaces;
using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Node.Features.Osds.Services;

public sealed class OsdsService
{
    private readonly IOsdRuntime _runtime;
    private readonly NodeConfig _config;
    private readonly Dictionary<int, OsdDto> _osds = [];

    public OsdsService(IOsdRuntime runtime, NodeConfig config)
    {
        _runtime = runtime;
        _config = config;
    }

    public IReadOnlyList<OsdDto> ListOsds()
    {
        SyncFromRuntime();
        return _osds.Values.Select(Refresh).ToArray();
    }

    public OsdDto? GetOsd(int osdId)
    {
        SyncFromRuntime();
        return _osds.TryGetValue(osdId, out var osd) ? Refresh(osd) : null;
    }

    public void Seed(OsdDto osd) => _osds[osd.OsdId] = osd;

    private void SyncFromRuntime()
    {
        foreach (var osdId in _runtime.ListIds())
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
    }

    public OsdMutationResult Start(OsdMutationRequest request)
    {
        var current = Require(request.OsdId);
        if (current.ProcessRunning)
            return new OsdMutationResult(request.OsdId, true, true);
        _runtime.Start(request.OsdId);
        _osds[request.OsdId] = current with { ProcessRunning = true, Up = true };
        return new OsdMutationResult(request.OsdId, true, false);
    }

    public OsdMutationResult Stop(OsdMutationRequest request)
    {
        var current = Require(request.OsdId);
        if (!current.ProcessRunning)
            return new OsdMutationResult(request.OsdId, false, true);
        _runtime.Stop(request.OsdId);
        _osds[request.OsdId] = current with { ProcessRunning = false, Up = false };
        return new OsdMutationResult(request.OsdId, false, false);
    }

    private OsdDto Refresh(OsdDto osd)
        => osd with { ProcessRunning = _runtime.IsRunning(osd.OsdId), HostId = _config.HostId };

    private OsdDto Require(int osdId)
        => GetOsd(osdId) ?? throw new InvalidOperationException($"Unknown OSD {osdId}.");
}

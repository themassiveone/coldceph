using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Osds.Interfaces;
using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Control.Features.Osds.Services;

public sealed class OsdsService
{
    private readonly INodeOsdsClient _node;
    private readonly ControlConfig _config;
    private readonly Dictionary<int, OsdDto> _osds = [];

    public OsdsService(INodeOsdsClient node, ControlConfig config)
    {
        _node = node;
        _config = config;
    }

    public IReadOnlyList<OsdDto> ListOsds() => _osds.Values.ToArray();

    public OsdDto? GetOsd(int osdId) => _osds.TryGetValue(osdId, out var osd) ? osd : null;

    public bool IsEveryProcessRunning() => _osds.Count > 0 && _osds.Values.All(osd => osd.ProcessRunning);

    public bool IsEveryProcessStopped() => _osds.Values.All(osd => !osd.ProcessRunning);

    public void Observe(IEnumerable<OsdDto> observed)
    {
        _osds.Clear();
        foreach (var osd in observed)
            _osds[osd.OsdId] = osd;
    }

    public void RefreshFromNode(Uri endpoint)
        => Observe(_node.List(endpoint));

    public void Seed(OsdDto osd) => _osds[osd.OsdId] = osd;

    public void StartAll(Uri nodeEndpoint, string operationId)
    {
        foreach (var osd in _osds.Values.ToArray())
        {
            var result = _node.Start(nodeEndpoint, new OsdMutationRequest
            {
                OsdId = osd.OsdId,
                OperationId = operationId,
                ControllerIdentity = _config.ControllerIdentity,
                Deadline = DateTimeOffset.UtcNow.AddMinutes(5),
                DesiredRunning = true
            });
            _osds[osd.OsdId] = osd with { ProcessRunning = result.ProcessRunning, Up = result.ProcessRunning };
        }
    }

    public void StopAll(Uri nodeEndpoint, string operationId)
    {
        foreach (var osd in _osds.Values.ToArray())
        {
            var result = _node.Stop(nodeEndpoint, new OsdMutationRequest
            {
                OsdId = osd.OsdId,
                OperationId = operationId,
                ControllerIdentity = _config.ControllerIdentity,
                Deadline = DateTimeOffset.UtcNow.AddMinutes(5),
                DesiredRunning = false
            });
            _osds[osd.OsdId] = osd with { ProcessRunning = result.ProcessRunning, Up = result.ProcessRunning };
        }
    }
}

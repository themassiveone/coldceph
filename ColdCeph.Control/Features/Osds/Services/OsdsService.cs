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

    public void Seed(OsdDto osd) => _osds[osd.OsdId] = osd;

    public void RefreshFromNode(string hostId, Uri endpoint)
    {
        var observed = _node.List(endpoint);
        foreach (var existing in _osds.Where(pair => pair.Value.HostId == hostId).Select(pair => pair.Key).ToArray())
            _osds.Remove(existing);
        foreach (var osd in observed)
            _osds[osd.OsdId] = osd with { HostId = hostId };
    }

    public void StartAll(string hostId, Uri nodeEndpoint, string operationId)
        => MutateHost(hostId, nodeEndpoint, operationId, desiredRunning: true);

    public void StopAll(string hostId, Uri nodeEndpoint, string operationId)
        => MutateHost(hostId, nodeEndpoint, operationId, desiredRunning: false);

    private void MutateHost(string hostId, Uri nodeEndpoint, string operationId, bool desiredRunning)
    {
        foreach (var osd in _osds.Values.Where(candidate => candidate.HostId == hostId).ToArray())
        {
            var request = new OsdMutationRequest
            {
                OsdId = osd.OsdId,
                OperationId = operationId,
                ControllerIdentity = _config.ControllerIdentity,
                Deadline = DateTimeOffset.UtcNow.AddMinutes(5),
                DesiredRunning = desiredRunning
            };
            var result = desiredRunning
                ? _node.Start(nodeEndpoint, request)
                : _node.Stop(nodeEndpoint, request);
            _osds[osd.OsdId] = osd with { ProcessRunning = result.ProcessRunning, Up = result.ProcessRunning };
        }
    }
}

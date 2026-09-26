using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Osds.Interfaces;
using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Control.Features.Osds.Services;

public sealed class OsdsService
{
    private readonly IAgentOsdsClient _agent;
    private readonly ControlConfig _config;
    private readonly Dictionary<int, OsdDto> _osds = [];

    public OsdsService(IAgentOsdsClient agent, ControlConfig config)
    {
        _agent = agent;
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

    public void Seed(OsdDto osd) => _osds[osd.OsdId] = osd;

    public void StartAll(Uri agentEndpoint, string operationId)
    {
        foreach (var osd in _osds.Values.ToArray())
        {
            var result = _agent.Start(agentEndpoint, new OsdMutationRequest
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

    public void StopAll(Uri agentEndpoint, string operationId)
    {
        foreach (var osd in _osds.Values.ToArray())
        {
            var result = _agent.Stop(agentEndpoint, new OsdMutationRequest
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

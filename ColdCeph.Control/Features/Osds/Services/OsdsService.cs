using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Osds.Interfaces;
using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Control.Features.Osds.Services;

public sealed class OsdsService
{
    private readonly INodeOsdsClient _node;
    private readonly ControlConfig _config;
    private readonly IOsdsObservationRepository? _repository;
    private readonly Dictionary<string, OsdDto> _osds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _hostErrors = new(StringComparer.Ordinal);

    public OsdsService(INodeOsdsClient node, ControlConfig config, IOsdsObservationRepository? repository = null)
    {
        _node = node;
        _config = config;
        _repository = repository;
        if (repository is null)
            return;
        foreach (var osd in repository.LoadOsds())
            _osds[Key(osd.HostId, osd.OsdId)] = osd;
        foreach (var error in repository.LoadErrors())
            _hostErrors[error.Key] = error.Value;
    }

    public IReadOnlyList<OsdDto> ListOsds() => _osds.Values.ToArray();

    public OsdDto? GetOsd(int osdId)
        => _osds.Values.FirstOrDefault(osd => osd.OsdId == osdId);

    public IReadOnlyList<string> ListObservationErrors()
        => _hostErrors.Select(pair => $"{pair.Key}: {pair.Value}").ToArray();

    public bool IsEveryProcessRunning() => _osds.Count > 0 && _osds.Values.All(osd => osd.ProcessRunning);

    public bool IsEveryProcessStopped() => _osds.Values.All(osd => !osd.ProcessRunning);

    public void Seed(OsdDto osd)
    {
        _osds[Key(osd.HostId, osd.OsdId)] = osd;
        Persist();
    }

    public void ApplyObserved(HostOsdsObservationDto observation)
    {
        foreach (var existing in _osds.Where(pair => pair.Value.HostId == observation.HostId).Select(pair => pair.Key).ToArray())
            _osds.Remove(existing);

        foreach (var osd in observation.Osds)
            _osds[Key(observation.HostId, osd.OsdId)] = osd with { HostId = observation.HostId };

        if (string.IsNullOrWhiteSpace(observation.Error))
            _hostErrors.Remove(observation.HostId);
        else
            _hostErrors[observation.HostId] = observation.Error;
        Persist();
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
            _osds[Key(hostId, osd.OsdId)] = osd with { ProcessRunning = result.ProcessRunning, Up = result.ProcessRunning };
        }
        Persist();
    }

    private void Persist()
        => _repository?.Save(_osds.Values.ToArray(), _hostErrors);

    private static string Key(string hostId, int osdId) => $"{hostId}\u001f{osdId}";
}

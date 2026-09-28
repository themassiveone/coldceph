using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Osds.Interfaces;
using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Control.Features.Osds.Services;

/// <summary>
/// Control's view of OSD inventory.
/// <para>
/// This is a singleton written by node pushes arriving on HTTP threads and read by the reconcile
/// loops and every operator page render, so all access to the dictionaries goes through
/// <c>_gate</c>. Node's matching services do the same; Control's did not, and concurrent
/// <c>Dictionary</c> access there can throw or tear a read.
/// </para>
/// </summary>
public sealed class OsdsService
{
    private readonly INodeOsdsClient _node;
    private readonly ControlConfig _config;
    private readonly IOsdsObservationRepository? _repository;
    private readonly object _gate = new();
    private readonly Dictionary<string, OsdDto> _osds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _hostErrors = new(StringComparer.Ordinal);

    public OsdsService(INodeOsdsClient node, ControlConfig config, IOsdsObservationRepository? repository = null)
    {
        _node = node;
        _config = config;
        _repository = repository;
        if (repository is null)
            return;
        lock (_gate)
        {
            foreach (var osd in repository.LoadOsds())
                _osds[Key(osd.HostId, osd.OsdId)] = osd;
            foreach (var error in repository.LoadErrors())
                _hostErrors[error.Key] = error.Value;
        }
    }

    public IReadOnlyList<OsdDto> ListOsds()
    {
        lock (_gate)
            return _osds.Values.ToArray();
    }

    public OsdDto? GetOsd(int osdId)
    {
        lock (_gate)
            return _osds.Values.FirstOrDefault(osd => osd.OsdId == osdId);
    }

    public IReadOnlyList<string> ListObservationErrors()
    {
        lock (_gate)
            return _hostErrors.Select(pair => $"{pair.Key}: {pair.Value}").ToArray();
    }

    /// <summary>
    /// True only when at least one OSD is known and all of them are running. An empty inventory
    /// says nothing about the plane, so it must not read as ready.
    /// </summary>
    public bool IsEveryProcessRunning()
    {
        lock (_gate)
            return _osds.Count > 0 && _osds.Values.All(osd => osd.ProcessRunning);
    }

    public bool IsEveryProcessStopped()
    {
        lock (_gate)
            return _osds.Values.All(osd => !osd.ProcessRunning);
    }

    public void Seed(OsdDto osd)
    {
        lock (_gate)
        {
            _osds[Key(osd.HostId, osd.OsdId)] = osd;
            PersistLocked();
        }
    }

    public void ApplyObserved(HostOsdsObservationDto observation)
    {
        lock (_gate)
        {
            // A node that reports an error but cannot enumerate must not erase what Control
            // already knows: an empty inventory reads as "every process stopped" and would let
            // the plane conclude it is safely asleep.
            var keepExisting = observation.Osds.Count == 0 && !string.IsNullOrWhiteSpace(observation.Error);
            if (!keepExisting)
            {
                foreach (var existing in _osds
                             .Where(pair => pair.Value.HostId == observation.HostId)
                             .Select(pair => pair.Key)
                             .ToArray())
                    _osds.Remove(existing);

                foreach (var osd in observation.Osds)
                    _osds[Key(observation.HostId, osd.OsdId)] = osd with { HostId = observation.HostId };
            }

            if (string.IsNullOrWhiteSpace(observation.Error))
                _hostErrors.Remove(observation.HostId);
            else
                _hostErrors[observation.HostId] = observation.Error;
            PersistLocked();
        }
    }

    /// <summary>
    /// Starts only the OSDs on this host that are not already running, and only those whose
    /// <paramref name="startable"/> gate allows it (Devices reports the mapped disk awake).
    /// Reissuing start for an OSD that is already running turned the wake into a command storm:
    /// one call per OSD per host every second for the whole of WAKING.
    /// </summary>
    public void StartDrifted(string hostId, Uri nodeEndpoint, string operationId, Func<OsdDto, bool> startable)
        => MutateDrifted(hostId, nodeEndpoint, operationId, desiredRunning: true, startable);

    public void StopDrifted(string hostId, Uri nodeEndpoint, string operationId)
        => MutateDrifted(hostId, nodeEndpoint, operationId, desiredRunning: false, _ => true);

    private void MutateDrifted(
        string hostId,
        Uri nodeEndpoint,
        string operationId,
        bool desiredRunning,
        Func<OsdDto, bool> allowed)
    {
        OsdDto[] drifted;
        lock (_gate)
            drifted = _osds.Values
                .Where(osd => osd.HostId == hostId && osd.ProcessRunning != desiredRunning && allowed(osd))
                .ToArray();

        foreach (var osd in drifted)
        {
            var request = new OsdMutationRequest
            {
                OsdId = osd.OsdId,
                OperationId = operationId,
                ControllerIdentity = _config.ControllerIdentity,
                Deadline = DateTimeOffset.UtcNow.AddMinutes(5),
                DesiredRunning = desiredRunning
            };

            // Outside the lock: this is a blocking HTTP call to another machine, and holding
            // the gate across it would stall every page render and node push.
            var result = desiredRunning
                ? _node.Start(nodeEndpoint, request)
                : _node.Stop(nodeEndpoint, request);

            lock (_gate)
            {
                if (_osds.TryGetValue(Key(hostId, osd.OsdId), out var current))
                    _osds[Key(hostId, osd.OsdId)] =
                        current with { ProcessRunning = result.ProcessRunning, Up = result.ProcessRunning };
            }
        }

        if (drifted.Length == 0)
            return;
        lock (_gate)
            PersistLocked();
    }

    private void PersistLocked()
        => _repository?.Save(_osds.Values.ToArray(), _hostErrors);

    private static string Key(string hostId, int osdId) => $"{hostId}\u001f{osdId}";
}

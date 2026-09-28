using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Devices.Interfaces;
using ColdCeph.Core.Features.Devices.DTOs;

namespace ColdCeph.Control.Features.Devices.Services;

/// <summary>
/// Control's view of disk inventory. Written by node pushes on HTTP threads, read by the
/// reconcile loops and every page render, so all dictionary access is under <c>_gate</c>.
/// </summary>
public sealed class DevicesService
{
    private readonly INodeDevicesClient _node;
    private readonly ControlConfig _config;
    private readonly IDevicesObservationRepository? _repository;
    private readonly object _gate = new();
    private readonly Dictionary<string, DeviceDto> _devices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _hostErrors = new(StringComparer.Ordinal);

    public DevicesService(INodeDevicesClient node, ControlConfig config, IDevicesObservationRepository? repository = null)
    {
        _node = node;
        _config = config;
        _repository = repository;
        if (repository is null)
            return;
        lock (_gate)
        {
            foreach (var device in repository.LoadDevices())
                _devices[device.DeviceId] = device;
            foreach (var error in repository.LoadErrors())
                _hostErrors[error.Key] = error.Value;
        }
    }

    public IReadOnlyList<DeviceDto> ListDevices()
    {
        lock (_gate)
            return _devices.Values.ToArray();
    }

    public DeviceDto? GetDevice(string deviceId)
    {
        lock (_gate)
            return _devices.TryGetValue(deviceId, out var device) ? device : null;
    }

    public IReadOnlyList<string> ListObservationErrors()
    {
        lock (_gate)
            return _hostErrors.Select(pair => $"{pair.Key}: {pair.Value}").ToArray();
    }

    public bool IsEveryDeviceStandby()
    {
        lock (_gate)
            return _devices.Values.All(device => device.PowerState == DevicePowerState.Standby);
    }

    public void Seed(DeviceDto device)
    {
        lock (_gate)
        {
            _devices[device.DeviceId] = device;
            PersistLocked();
        }
    }

    public void ApplyObserved(HostDevicesObservationDto observation)
    {
        lock (_gate)
        {
            // As with OSDs: an error report carrying no devices must not erase inventory, or
            // "every device is in standby" becomes vacuously true.
            var keepExisting = observation.Devices.Count == 0 && !string.IsNullOrWhiteSpace(observation.Error);
            if (!keepExisting)
            {
                foreach (var existing in _devices
                             .Where(pair => pair.Value.HostId == observation.HostId)
                             .Select(pair => pair.Key)
                             .ToArray())
                    _devices.Remove(existing);

                foreach (var device in observation.Devices)
                    _devices[device.DeviceId] = device with { HostId = observation.HostId };
            }

            if (string.IsNullOrWhiteSpace(observation.Error))
                _hostErrors.Remove(observation.HostId);
            else
                _hostErrors[observation.HostId] = observation.Error;
            PersistLocked();
        }
    }

    /// <summary>Wakes only the disks on this host that are not already active.</summary>
    public void WakeDrifted(string hostId, Uri nodeEndpoint, string operationId)
        => MutateDrifted(hostId, nodeEndpoint, operationId, DevicePowerState.Active);

    public void StandbyDrifted(string hostId, Uri nodeEndpoint, string operationId)
        => MutateDrifted(hostId, nodeEndpoint, operationId, DevicePowerState.Standby);

    private void MutateDrifted(string hostId, Uri nodeEndpoint, string operationId, DevicePowerState desired)
    {
        DeviceDto[] drifted;
        lock (_gate)
            drifted = _devices.Values
                .Where(device => device.HostId == hostId && device.PowerState != desired)
                .ToArray();

        foreach (var device in drifted)
        {
            var request = new DeviceMutationRequest
            {
                DeviceId = device.DeviceId,
                OperationId = operationId,
                ControllerIdentity = _config.ControllerIdentity,
                Deadline = DateTimeOffset.UtcNow.AddMinutes(5),
                DesiredPowerState = desired
            };

            var result = desired == DevicePowerState.Active
                ? _node.Wake(nodeEndpoint, request)
                : _node.Standby(nodeEndpoint, request);

            lock (_gate)
            {
                if (_devices.TryGetValue(device.DeviceId, out var current))
                    _devices[device.DeviceId] = current with { PowerState = result.PowerState };
            }
        }

        if (drifted.Length == 0)
            return;
        lock (_gate)
            PersistLocked();
    }

    private void PersistLocked()
        => _repository?.Save(_devices.Values.ToArray(), _hostErrors);
}

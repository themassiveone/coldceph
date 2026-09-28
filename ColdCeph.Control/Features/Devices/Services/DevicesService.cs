using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Devices.Interfaces;
using ColdCeph.Core.Features.Devices.DTOs;

namespace ColdCeph.Control.Features.Devices.Services;

public sealed class DevicesService
{
    private readonly INodeDevicesClient _node;
    private readonly ControlConfig _config;
    private readonly IDevicesObservationRepository? _repository;
    private readonly Dictionary<string, DeviceDto> _devices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _hostErrors = new(StringComparer.Ordinal);

    public DevicesService(INodeDevicesClient node, ControlConfig config, IDevicesObservationRepository? repository = null)
    {
        _node = node;
        _config = config;
        _repository = repository;
        if (repository is null)
            return;
        foreach (var device in repository.LoadDevices())
            _devices[device.DeviceId] = device;
        foreach (var error in repository.LoadErrors())
            _hostErrors[error.Key] = error.Value;
    }

    public IReadOnlyList<DeviceDto> ListDevices() => _devices.Values.ToArray();

    public DeviceDto? GetDevice(string deviceId)
        => _devices.TryGetValue(deviceId, out var device) ? device : null;

    public IReadOnlyList<string> ListObservationErrors()
        => _hostErrors.Select(pair => $"{pair.Key}: {pair.Value}").ToArray();

    public bool IsEveryDeviceStandby()
        => _devices.Values.All(device => device.PowerState == DevicePowerState.Standby);

    public void Seed(DeviceDto device)
    {
        _devices[device.DeviceId] = device;
        Persist();
    }

    public void ApplyObserved(HostDevicesObservationDto observation)
    {
        foreach (var existing in _devices.Where(pair => pair.Value.HostId == observation.HostId).Select(pair => pair.Key).ToArray())
            _devices.Remove(existing);

        foreach (var device in observation.Devices)
            _devices[device.DeviceId] = device with { HostId = observation.HostId };

        if (string.IsNullOrWhiteSpace(observation.Error))
            _hostErrors.Remove(observation.HostId);
        else
            _hostErrors[observation.HostId] = observation.Error;
        Persist();
    }

    public void WakeAll(string hostId, Uri nodeEndpoint, string operationId)
        => MutateHost(hostId, nodeEndpoint, operationId, DevicePowerState.Active);

    public void StandbyAll(string hostId, Uri nodeEndpoint, string operationId)
        => MutateHost(hostId, nodeEndpoint, operationId, DevicePowerState.Standby);

    private void MutateHost(string hostId, Uri nodeEndpoint, string operationId, DevicePowerState desired)
    {
        foreach (var device in _devices.Values.Where(candidate => candidate.HostId == hostId).ToArray())
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
            _devices[device.DeviceId] = device with { PowerState = result.PowerState };
        }
        Persist();
    }

    private void Persist()
        => _repository?.Save(_devices.Values.ToArray(), _hostErrors);
}

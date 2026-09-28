using ColdCeph.Core.Features.Devices.DTOs;

namespace ColdCeph.Control.Features.Devices.Interfaces;

public interface IDevicesObservationRepository
{
    IReadOnlyList<DeviceDto> LoadDevices();
    IReadOnlyDictionary<string, string> LoadErrors();
    void Save(IReadOnlyList<DeviceDto> devices, IReadOnlyDictionary<string, string> errors);
}

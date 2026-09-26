using ColdCeph.Core.Features.Devices.DTOs;

namespace ColdCeph.Control.Features.Devices.Interfaces;

public interface INodeDevicesClient
{
    IReadOnlyList<DeviceDto> List(Uri endpoint);
    DeviceMutationResult Wake(Uri endpoint, DeviceMutationRequest request);
    DeviceMutationResult Standby(Uri endpoint, DeviceMutationRequest request);
}

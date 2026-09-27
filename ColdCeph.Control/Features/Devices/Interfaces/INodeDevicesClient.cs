using ColdCeph.Core.Features.Devices.DTOs;

namespace ColdCeph.Control.Features.Devices.Interfaces;

public interface INodeDevicesClient
{
    DeviceMutationResult Wake(Uri endpoint, DeviceMutationRequest request);
    DeviceMutationResult Standby(Uri endpoint, DeviceMutationRequest request);
}

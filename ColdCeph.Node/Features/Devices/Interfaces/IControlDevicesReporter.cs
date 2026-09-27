using ColdCeph.Core.Features.Devices.DTOs;

namespace ColdCeph.Node.Features.Devices.Interfaces;

public interface IControlDevicesReporter
{
    void Send(HostDevicesObservationDto observation);
}

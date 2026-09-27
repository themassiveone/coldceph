using ColdCeph.Node.Features.Devices.Interfaces;
using ColdCeph.Core.Features.Devices.DTOs;

namespace ColdCeph.Node.Features.Devices.Providers;

public sealed class NoopControlDevicesReporter : IControlDevicesReporter
{
    public void Send(HostDevicesObservationDto observation)
    {
    }
}

using ColdCeph.Node.Features.Osds.Interfaces;
using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Node.Features.Osds.Providers;

public sealed class NoopControlOsdsReporter : IControlOsdsReporter
{
    public void Send(HostOsdsObservationDto observation)
    {
    }
}

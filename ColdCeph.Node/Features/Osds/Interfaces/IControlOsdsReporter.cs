using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Node.Features.Osds.Interfaces;

public interface IControlOsdsReporter
{
    void Send(HostOsdsObservationDto observation);
}

using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Agent.Features.Osds.Interfaces;

public interface IOsdRuntime
{
    bool IsRunning(int osdId);
    void Start(int osdId);
    void Stop(int osdId);
}

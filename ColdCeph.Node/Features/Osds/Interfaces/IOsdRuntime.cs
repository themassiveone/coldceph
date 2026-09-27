using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Node.Features.Osds.Interfaces;

public interface IOsdRuntime
{
    IReadOnlyList<int> ListIds();
    bool IsRunning(int osdId);
    void Start(int osdId);
    void Stop(int osdId);
}

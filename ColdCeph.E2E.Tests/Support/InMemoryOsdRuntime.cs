using ColdCeph.Node.Features.Osds.Interfaces;

namespace ColdCeph.E2E.Tests.Support;

public sealed class InMemoryOsdRuntime : IOsdRuntime
{
    private readonly HashSet<int> _known = [];
    private readonly HashSet<int> _running = [];

    public IReadOnlyList<int> ListIds() => _known.ToArray();

    public void Know(int osdId) => _known.Add(osdId);

    public bool IsRunning(int osdId) => _running.Contains(osdId);

    public void Start(int osdId)
    {
        _known.Add(osdId);
        _running.Add(osdId);
    }

    public void Stop(int osdId) => _running.Remove(osdId);
}

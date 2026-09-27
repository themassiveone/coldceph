using ColdCeph.Node.Features.Osds.Interfaces;
using ColdCeph.Node.Shared;

namespace ColdCeph.Node.Features.Osds.Providers;

public sealed class DockerExecOsdRuntime : IOsdRuntime
{
    private readonly IProcessRunner _runner;
    private readonly string _container;

    public DockerExecOsdRuntime(IProcessRunner runner, string container)
    {
        _runner = runner;
        _container = container;
    }

    public IReadOnlyList<int> ListIds()
    {
        var output = _runner.Run("docker", [
            "exec", _container, "bash", "-c",
            "cat /var/lib/ceph/osd/*/whoami 2>/dev/null || true"
        ]);
        return output
            .Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, out var id) ? id : (int?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
    }

    public bool IsRunning(int osdId)
    {
        try
        {
            _runner.Run("docker", ["exec", _container, "pgrep", "-x", "ceph-osd"]);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public void Start(int osdId)
        => _runner.Run("docker", [
            "exec", _container, "bash", "-c",
            "printf running > /var/run/ceph/osd.wanted"
        ]);

    public void Stop(int osdId)
        => _runner.Run("docker", [
            "exec", _container, "bash", "-c",
            "printf stopped > /var/run/ceph/osd.wanted; pkill -x ceph-osd || true"
        ]);
}

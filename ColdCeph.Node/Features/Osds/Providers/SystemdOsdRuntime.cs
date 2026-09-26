using ColdCeph.Node.Features.Osds.Interfaces;
using ColdCeph.Node.Shared;

namespace ColdCeph.Node.Features.Osds.Providers;

public sealed class SystemdOsdRuntime : IOsdRuntime
{
    private readonly IProcessRunner _runner;

    public SystemdOsdRuntime(IProcessRunner runner)
    {
        _runner = runner;
    }

    public bool IsRunning(int osdId)
    {
        try
        {
            var output = _runner.Run("systemctl", ["is-active", $"ceph-osd@{osdId}"]);
            return output.Trim() == "active";
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public void Start(int osdId)
        => _runner.Run("systemctl", ["start", $"ceph-osd@{osdId}"]);

    public void Stop(int osdId)
        => _runner.Run("systemctl", ["stop", $"ceph-osd@{osdId}"]);
}

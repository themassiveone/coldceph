using ColdCeph.Node.Composition;
using ColdCeph.Node.Features.Devices.Services;
using ColdCeph.Node.Features.Osds.Controllers;
using ColdCeph.Node.Features.Osds.Interfaces;
using ColdCeph.Node.Features.Osds.Services;
using ColdCeph.Core.Features.Devices.DTOs;
using ColdCeph.Core.Features.Operations.DTOs;
using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Node.Tests.Fake;

public sealed class FakeOsdRuntime : IOsdRuntime
{
    public HashSet<int> Running { get; } = [];
    public List<string> Commands { get; } = [];

    public bool IsRunning(int osdId) => Running.Contains(osdId);

    public void Start(int osdId)
    {
        Commands.Add($"start {osdId}");
        Running.Add(osdId);
    }

    public void Stop(int osdId)
    {
        Commands.Add($"stop {osdId}");
        Running.Remove(osdId);
    }
}

public sealed class FakeDiskPower : ColdCeph.Node.Features.Devices.Interfaces.IDiskPower
{
    public DevicePowerState State { get; set; } = DevicePowerState.Active;
    public List<string> Commands { get; } = [];

    public DevicePowerState GetPowerState(string deviceId, string? path) => State;

    public void Wake(string? path)
    {
        Commands.Add("wake");
        State = DevicePowerState.Active;
    }

    public void Standby(string? path)
    {
        Commands.Add("standby");
        State = DevicePowerState.Standby;
    }
}

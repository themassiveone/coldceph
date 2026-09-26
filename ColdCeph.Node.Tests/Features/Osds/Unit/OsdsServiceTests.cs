using ColdCeph.Node.Composition;
using ColdCeph.Node.Features.Osds.Services;
using ColdCeph.Node.Tests.Fake;
using ColdCeph.Core.Features.Operations.DTOs;
using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Node.Tests.Features.Osds.Unit;

[TestFixture]
public sealed class OsdsServiceTests
{
    [Test]
    public void Stop_is_idempotent_when_already_stopped()
    {
        var runtime = new FakeOsdRuntime();
        var osds = new OsdsService(runtime, new NodeConfig { HostId = "h1" });
        osds.Seed(new OsdDto { OsdId = 3, HostId = "h1", DeviceId = "d", Up = false, In = true, ProcessRunning = false });

        var result = osds.Stop(new OsdMutationRequest
        {
            OsdId = 3,
            OperationId = OperationIdRules.Create().Value,
            ControllerIdentity = "control",
            Deadline = DateTimeOffset.UtcNow.AddMinutes(1),
            DesiredRunning = false
        });

        Assert.That(result.IdempotentHit, Is.True);
        Assert.That(result.ProcessRunning, Is.False);
        Assert.That(runtime.Commands, Is.Empty);
    }

    [Test]
    public void Start_rejects_unknown_osd()
    {
        var osds = new OsdsService(new FakeOsdRuntime(), new NodeConfig { HostId = "h1" });

        Assert.That(() => osds.Start(new OsdMutationRequest
        {
            OsdId = 9,
            OperationId = OperationIdRules.Create().Value,
            ControllerIdentity = "control",
            Deadline = DateTimeOffset.UtcNow.AddMinutes(1),
            DesiredRunning = true
        }), Throws.InvalidOperationException);
    }
}

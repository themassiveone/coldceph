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
        runtime.Listed.Add(3);
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

    [Test]
    public void ListOsds_includes_ids_reported_by_the_runtime()
    {
        var runtime = new FakeOsdRuntime();
        runtime.Listed.Add(4);
        var osds = new OsdsService(runtime, new NodeConfig { HostId = "node-a" });

        Assert.That(osds.ListOsds().Select(osd => osd.OsdId), Does.Contain(4));
        Assert.That(osds.ListOsds().Single(osd => osd.OsdId == 4).HostId, Is.EqualTo("node-a"));
    }

    [Test]
    public void ListOsds_does_not_invent_ids_when_the_runtime_lists_none()
    {
        var osds = new OsdsService(new FakeOsdRuntime(), new NodeConfig { HostId = "node-a" });

        Assert.That(osds.ListOsds(), Is.Empty);
    }

    [Test]
    public void ListOsds_keeps_known_ids_when_discovery_fails()
    {
        var runtime = new FakeOsdRuntime();
        runtime.Listed.Add(4);
        var osds = new OsdsService(runtime, new NodeConfig { HostId = "node-a" });
        Assert.That(osds.ListOsds().Select(osd => osd.OsdId), Does.Contain(4));
        runtime.ThrowOnList = true;

        var listed = osds.ListOsds();

        Assert.That(listed.Select(osd => osd.OsdId), Does.Contain(4));
        Assert.That(osds.GetLastDiscoveryError(), Does.Contain("docker exec failed"));
    }

    [Test]
    public void ListOsds_does_not_keep_an_error_after_discovery_succeeds()
    {
        var runtime = new FakeOsdRuntime { ThrowOnList = true };
        var osds = new OsdsService(runtime, new NodeConfig { HostId = "node-a" });
        _ = osds.ListOsds();
        runtime.ThrowOnList = false;
        runtime.Listed.Add(4);
        _ = osds.ListOsds();

        Assert.That(osds.GetLastDiscoveryError(), Is.Null);
        Assert.That(osds.ListOsds().Select(osd => osd.OsdId), Does.Contain(4));
    }

    [Test]
    public void ListOsds_does_not_throw_when_called_from_many_threads()
    {
        var runtime = new FakeOsdRuntime();
        runtime.Listed.Add(1);
        runtime.Listed.Add(2);
        var osds = new OsdsService(runtime, new NodeConfig { HostId = "node-b" });

        Parallel.For(0, 64, _ => osds.ListOsds());

        Assert.That(osds.ListOsds().Select(osd => osd.OsdId), Is.EquivalentTo(new[] { 1, 2 }));
    }

    [Test]
    public void ListOsds_does_not_lose_ids_while_start_runs()
    {
        var runtime = new FakeOsdRuntime();
        runtime.Listed.Add(2);
        var osds = new OsdsService(runtime, new NodeConfig { HostId = "node-c" });
        osds.ListOsds();

        Parallel.Invoke(
            () => Parallel.For(0, 32, _ => osds.ListOsds()),
            () => osds.Start(new OsdMutationRequest
            {
                OsdId = 2,
                OperationId = OperationIdRules.Create().Value,
                ControllerIdentity = "control",
                Deadline = DateTimeOffset.UtcNow.AddMinutes(1),
                DesiredRunning = true
            }));

        Assert.That(osds.ListOsds().Select(osd => osd.OsdId), Does.Contain(2));
        Assert.That(osds.GetOsd(2)?.ProcessRunning, Is.True);
    }
}

using ColdCeph.Node.Composition;
using ColdCeph.Node.Features.Hosts.Controllers;
using ColdCeph.Node.Features.Hosts.Services;
using ColdCeph.Node.Features.Osds.Controllers;
using ColdCeph.Node.Features.Osds.Interfaces;
using ColdCeph.Node.Features.Osds.Services;
using ColdCeph.Node.Tests.Fake;
using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Node.Tests.Features.Osds.Unit;

[TestFixture]
public sealed class OsdsReportLoopTests
{
    [Test]
    public void ReportOnce_sends_inventory_after_enroll()
    {
        var (loop, reporter, runtime, hosts) = Create();
        runtime.Listed.Add(0);
        hosts.NoteJoinStatus(200);

        loop.ReportOnce();

        Assert.That(reporter.Sent, Has.Count.EqualTo(1));
        Assert.That(reporter.Sent[0].Osds.Select(osd => osd.OsdId), Does.Contain(0));
        Assert.That(reporter.Sent[0].Error, Is.Null);
    }

    [Test]
    public void ReportOnce_does_not_send_when_not_enrolled()
    {
        var (loop, reporter, runtime, _) = Create();
        runtime.Listed.Add(0);

        loop.ReportOnce();

        Assert.That(reporter.Sent, Is.Empty);
    }

    [Test]
    public void ReportOnce_does_not_resend_an_unchanged_snapshot()
    {
        var (loop, reporter, runtime, hosts) = Create();
        runtime.Listed.Add(0);
        hosts.NoteJoinStatus(200);

        loop.ReportOnce();
        loop.ReportOnce();

        Assert.That(reporter.Sent, Has.Count.EqualTo(1));
    }

    [Test]
    public void ReportOnce_sends_again_after_start()
    {
        var (loop, reporter, runtime, hosts, osds) = CreateWithOsds();
        runtime.Listed.Add(0);
        hosts.NoteJoinStatus(200);
        loop.ReportOnce();

        osds.Start(new OsdMutationRequest
        {
            OsdId = 0,
            OperationId = "op",
            ControllerIdentity = "control",
            Deadline = DateTimeOffset.UtcNow.AddMinutes(1),
            DesiredRunning = true
        });
        loop.ReportOnce();

        Assert.That(reporter.Sent, Has.Count.EqualTo(2));
        Assert.That(reporter.Sent[1].Osds.Single().ProcessRunning, Is.True);
    }

    [Test]
    public void ReportOnce_includes_a_discovery_error()
    {
        var (loop, reporter, runtime, hosts) = Create();
        runtime.ThrowOnList = true;
        hosts.NoteJoinStatus(200);

        loop.ReportOnce();

        Assert.That(reporter.Sent, Has.Count.EqualTo(1));
        Assert.That(reporter.Sent[0].Error, Does.Contain("docker exec failed"));
    }

    private static (OsdsReportLoop Loop, RecordingReporter Reporter, FakeOsdRuntime Runtime, HostsController Hosts) Create()
    {
        var (loop, reporter, runtime, hosts, _) = CreateWithOsds();
        return (loop, reporter, runtime, hosts);
    }

    private static (OsdsReportLoop Loop, RecordingReporter Reporter, FakeOsdRuntime Runtime, HostsController Hosts, OsdsService Osds)
        CreateWithOsds()
    {
        var config = new NodeConfig
        {
            HostId = "dev",
            ControlEndpoint = new Uri("http://127.0.0.1:8080")
        };
        var runtime = new FakeOsdRuntime();
        var osds = new OsdsService(runtime, config);
        var hosts = new HostsController(new HostsService(config));
        var reporter = new RecordingReporter();
        var loop = new OsdsReportLoop(new OsdsController(osds), hosts, reporter, config);
        return (loop, reporter, runtime, hosts, osds);
    }

    private sealed class RecordingReporter : IControlOsdsReporter
    {
        public List<HostOsdsObservationDto> Sent { get; } = [];

        public void Send(HostOsdsObservationDto observation) => Sent.Add(observation);
    }
}

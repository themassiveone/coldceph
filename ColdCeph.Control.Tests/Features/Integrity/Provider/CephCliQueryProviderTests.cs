using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Integrity.Controllers;
using ColdCeph.Control.Features.Integrity.Providers;
using ColdCeph.Control.Tests.Fake;

namespace ColdCeph.Control.Tests.Features.Integrity.Provider;

[TestFixture]
public sealed class CephCliQueryProviderTests
{
    [Test]
    public void Health_detail_uses_json_format()
    {
        var runner = new RecordingProcessRunner { Output = """{"status":"HEALTH_OK","checks":{}}""" };
        var provider = new CephCliQueryProvider(new ControlConfig { CephBinary = "ceph" }, runner);

        var health = provider.GetHealthDetail();

        Assert.That(runner.Commands.Any(command => command == "ceph --format json health detail"), Is.True);
        Assert.That(health.Status, Is.EqualTo("HEALTH_OK"));
    }

    [Test]
    public void Provider_does_not_issue_ok_to_stop()
    {
        var runner = new RecordingProcessRunner { Output = """{"status":"HEALTH_OK","checks":{}}""" };
        var provider = new CephCliQueryProvider(new ControlConfig { CephBinary = "ceph" }, runner);

        _ = provider.GetHealthDetail();
        _ = provider.GetQuorumAvailable();
        _ = provider.GetPgsClean();

        Assert.That(runner.Commands.Any(command => command.Contains("ok-to-stop")), Is.False);
    }
}

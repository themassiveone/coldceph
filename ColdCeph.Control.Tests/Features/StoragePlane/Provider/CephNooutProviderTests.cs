using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.StoragePlane.Providers;
using ColdCeph.Control.Tests.Fake;

namespace ColdCeph.Control.Tests.Features.StoragePlane.Provider;

[TestFixture]
public sealed class CephNooutProviderTests
{
    [Test]
    public void SetGroupNoout_uses_scoped_set_group()
    {
        var runner = new RecordingProcessRunner();
        var provider = new CephNooutProvider(new ControlConfig { CephBinary = "ceph" }, runner);

        provider.SetGroupNoout("hdd-osds");

        Assert.That(runner.Commands, Is.EqualTo(new[] { "ceph osd set-group noout hdd-osds" }));
    }

    [Test]
    public void Provider_does_not_emit_out_or_destroy_verbs()
    {
        var runner = new RecordingProcessRunner();
        var provider = new CephNooutProvider(new ControlConfig { CephBinary = "ceph" }, runner);

        provider.SetGroupNoout("hdd-osds");
        provider.UnsetGroupNoout("hdd-osds");

        Assert.That(runner.Commands.Any(command => command.Contains("osd out") || command.Contains("destroy") || command.Contains("purge") || command.Contains("osd rm")), Is.False);
    }
}

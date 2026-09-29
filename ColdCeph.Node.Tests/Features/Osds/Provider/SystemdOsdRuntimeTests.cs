using ColdCeph.Node.Features.Osds.Providers;
using ColdCeph.Node.Shared;

namespace ColdCeph.Node.Tests.Features.Osds.Provider;

[TestFixture]
public sealed class SystemdOsdRuntimeTests
{
    [Test]
    public void Start_uses_systemd_unit_name()
    {
        var runner = new RecordingRunner { Output = "active\n" };
        var runtime = new SystemdOsdRuntime(runner);

        runtime.Start(7);

        Assert.That(runner.Commands, Is.EqualTo(new[] { "systemctl start ceph-osd@7" }));
    }

    /// <summary>
    /// The predicate the whole WAKING to READY transition rests on. It had only a negative case,
    /// so an implementation that always returned false would have passed.
    /// </summary>
    [Test]
    public void IsRunning_is_true_for_an_active_unit()
    {
        var runner = new RecordingRunner { Output = "active\n" };
        var runtime = new SystemdOsdRuntime(runner);

        Assert.That(runtime.IsRunning(7), Is.True);
        Assert.That(runner.Commands, Is.EqualTo(new[] { "systemctl is-active ceph-osd@7" }));
    }

    /// <summary>
    /// An OSD still coming up is not running yet. Reading "activating" as running would take the
    /// plane to READY before Ceph could serve.
    /// </summary>
    [TestCase("activating\n")]
    [TestCase("inactive\n")]
    [TestCase("failed\n")]
    [TestCase("unknown\n")]
    [TestCase("")]
    public void IsRunning_is_false_for_anything_other_than_active(string output)
    {
        var runner = new RecordingRunner { Output = output };
        var runtime = new SystemdOsdRuntime(runner);

        Assert.That(runtime.IsRunning(7), Is.False);
    }

    [Test]
    public void Stop_uses_the_systemd_unit_name()
    {
        var runner = new RecordingRunner { Output = "" };
        var runtime = new SystemdOsdRuntime(runner);

        runtime.Stop(7);

        Assert.That(runner.Commands, Is.EqualTo(new[] { "systemctl stop ceph-osd@7" }));
    }

    /// <summary>
    /// A failed start must reach the caller, so Control does not record the OSD as running.
    /// </summary>
    [Test]
    public void A_failing_start_propagates()
    {
        var runner = new RecordingRunner { Throw = true };
        var runtime = new SystemdOsdRuntime(runner);

        Assert.That(() => runtime.Start(7), Throws.InvalidOperationException);
    }

    [Test]
    public void A_failing_stop_propagates()
    {
        var runner = new RecordingRunner { Throw = true };
        var runtime = new SystemdOsdRuntime(runner);

        Assert.That(() => runtime.Stop(7), Throws.InvalidOperationException);
    }

    [Test]
    public void Nothing_here_removes_an_osd_from_the_cluster()
    {
        var runner = new RecordingRunner { Output = "active\n" };
        var runtime = new SystemdOsdRuntime(runner);

        _ = runtime.ListIds();
        _ = runtime.IsRunning(7);
        runtime.Start(7);
        runtime.Stop(7);

        foreach (var forbidden in new[] { "osd out", "purge", "destroy", "safe-to-destroy", "disable", "mask" })
            Assert.That(runner.Commands, Has.None.Contains(forbidden));
    }

    [Test]
    public void IsRunning_is_false_when_systemctl_fails()
    {
        var runner = new RecordingRunner { Throw = true };
        var runtime = new SystemdOsdRuntime(runner);

        Assert.That(runtime.IsRunning(7), Is.False);
    }

    [Test]
    public void ListIds_parses_systemd_unit_names()
    {
        var runner = new RecordingRunner { Output = "ceph-osd@3.service loaded active running\nceph-osd@4.service loaded inactive dead\n" };
        var runtime = new SystemdOsdRuntime(runner);

        Assert.That(runtime.ListIds(), Is.EquivalentTo(new[] { 3, 4 }));
    }

    [Test]
    public void ListIds_is_empty_when_systemctl_fails()
    {
        var runner = new RecordingRunner { Throw = true };
        var runtime = new SystemdOsdRuntime(runner);

        Assert.That(runtime.ListIds(), Is.Empty);
    }

    private sealed class RecordingRunner : IProcessRunner
    {
        public List<string> Commands { get; } = [];
        public string Output { get; set; } = "";
        public bool Throw { get; set; }

        public string Run(string fileName, IReadOnlyList<string> arguments)
        {
            Commands.Add($"{fileName} {string.Join(' ', arguments)}");
            if (Throw)
                throw new InvalidOperationException("inactive");
            return Output;
        }
    }
}

using ColdCeph.Node.Features.Osds.Providers;
using ColdCeph.Node.Shared;

namespace ColdCeph.Node.Tests.Features.Osds.Provider;

[TestFixture]
public sealed class DockerExecOsdRuntimeTests
{
    [Test]
    public void Stop_writes_wanted_stopped_and_signals_ceph_osd()
    {
        var runner = new RecordingRunner { Output = "" };
        var runtime = new DockerExecOsdRuntime(runner, "coldceph-node-a");

        runtime.Stop(2);

        Assert.That(runner.Commands.Single(), Does.Contain("docker exec coldceph-node-a"));
        Assert.That(runner.Commands.Single(), Does.Contain("osd.wanted"));
        Assert.That(runner.Commands.Single(), Does.Contain("pkill"));
    }

    [Test]
    public void IsRunning_is_true_when_the_osd_process_is_found()
    {
        var runner = new RecordingRunner { Output = "412\n" };
        var runtime = new DockerExecOsdRuntime(runner, "coldceph-node-a");

        Assert.That(runtime.IsRunning(2), Is.True);
        Assert.That(runner.Commands.Single(), Does.Contain("pgrep -x ceph-osd"));
    }

    [Test]
    public void Start_writes_wanted_running()
    {
        var runner = new RecordingRunner { Output = "" };
        var runtime = new DockerExecOsdRuntime(runner, "coldceph-node-a");

        runtime.Start(2);

        Assert.That(runner.Commands.Single(), Does.Contain("printf running"));
        Assert.That(runner.Commands.Single(), Does.Contain("osd.wanted"));
    }

    [Test]
    public void ListIds_is_empty_when_no_osd_is_present()
    {
        var runner = new RecordingRunner { Output = "\n" };
        var runtime = new DockerExecOsdRuntime(runner, "coldceph-node-a");

        Assert.That(runtime.ListIds(), Is.Empty);
    }

    [Test]
    public void ListIds_parses_several_osds()
    {
        var runner = new RecordingRunner { Output = "0\n3\n3\n" };
        var runtime = new DockerExecOsdRuntime(runner, "coldceph-node-a");

        Assert.That(runtime.ListIds(), Is.EquivalentTo(new[] { 0, 3 }));
    }

    [Test]
    public void Nothing_here_removes_an_osd_from_the_cluster()
    {
        var runner = new RecordingRunner { Output = "0\n" };
        var runtime = new DockerExecOsdRuntime(runner, "coldceph-node-a");

        _ = runtime.ListIds();
        runtime.Start(0);
        runtime.Stop(0);

        foreach (var forbidden in new[] { "osd out", "purge", "destroy", "safe-to-destroy", "rm " })
            Assert.That(runner.Commands, Has.None.Contains(forbidden));
    }

    [Test]
    public void IsRunning_is_false_when_docker_exec_fails()
    {
        var runner = new RecordingRunner { Throw = true };
        var runtime = new DockerExecOsdRuntime(runner, "coldceph-node-a");

        Assert.That(runtime.IsRunning(2), Is.False);
    }

    [Test]
    public void ListIds_parses_whoami_output()
    {
        var runner = new RecordingRunner { Output = "0\n" };
        var runtime = new DockerExecOsdRuntime(runner, "coldceph-node-a");

        Assert.That(runtime.ListIds(), Is.EqualTo(new[] { 0 }));
    }

    [Test]
    public void ListIds_throws_when_docker_exec_fails()
    {
        var runner = new RecordingRunner { Throw = true };
        var runtime = new DockerExecOsdRuntime(runner, "coldceph-node-a");

        Assert.That(() => runtime.ListIds(), Throws.InvalidOperationException);
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

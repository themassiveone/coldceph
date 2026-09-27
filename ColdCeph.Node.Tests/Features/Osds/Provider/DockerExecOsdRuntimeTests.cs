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
    public void IsRunning_is_false_when_docker_exec_fails()
    {
        var runner = new RecordingRunner { Throw = true };
        var runtime = new DockerExecOsdRuntime(runner, "coldceph-node-a");

        Assert.That(runtime.IsRunning(2), Is.False);
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

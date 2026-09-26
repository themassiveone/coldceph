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

    [Test]
    public void IsRunning_is_false_when_systemctl_fails()
    {
        var runner = new RecordingRunner { Throw = true };
        var runtime = new SystemdOsdRuntime(runner);

        Assert.That(runtime.IsRunning(7), Is.False);
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

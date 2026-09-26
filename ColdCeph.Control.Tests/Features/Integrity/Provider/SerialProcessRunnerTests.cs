using ColdCeph.Control.Shared;

namespace ColdCeph.Control.Tests.Features.Integrity.Provider;

[TestFixture]
public sealed class SerialProcessRunnerTests
{
    [Test]
    public void Concurrent_runs_never_overlap()
    {
        var inner = new OverlapRunner();
        var serial = new SerialProcessRunner(inner);

        Parallel.For(0, 8, _ => serial.Run("ceph", ["status"]));

        Assert.That(inner.MaxConcurrent, Is.EqualTo(1));
    }

    [Test]
    public void Inner_runner_without_serial_can_overlap()
    {
        var inner = new OverlapRunner();

        Parallel.For(0, 8, _ => inner.Run("ceph", ["status"]));

        Assert.That(inner.MaxConcurrent, Is.GreaterThan(1));
    }

    private sealed class OverlapRunner : IProcessRunner
    {
        private int _current;

        public int MaxConcurrent { get; private set; }

        public string Run(string fileName, IReadOnlyList<string> arguments)
        {
            var now = Interlocked.Increment(ref _current);
            lock (this)
                MaxConcurrent = Math.Max(MaxConcurrent, now);
            Thread.Sleep(30);
            Interlocked.Decrement(ref _current);
            return "";
        }
    }
}

using System.Diagnostics;
using ColdCeph.Control.Shared;
using ColdCeph.Control.Tests.Support;

namespace ColdCeph.Control.Tests.Features.Integrity.Provider;

/// <summary>
/// The runner had no tests at all, which left two hangs unguarded: an unbounded wait, and a
/// stdout-then-stderr read order that deadlocks on a chatty child. These run real child
/// processes, because a fake pipe cannot reproduce either.
/// </summary>
[TestFixture]
public sealed class SystemProcessRunnerTests
{
    [Test]
    public void Stdout_is_returned()
    {
        var runner = new SystemProcessRunner(TimeSpan.FromSeconds(10));

        var output = runner.Run("/bin/sh", ["-c", "echo hello"]);

        Assert.That(output.Trim(), Is.EqualTo("hello"));
    }

    [Test]
    public void A_non_zero_exit_throws_with_stderr_in_the_message()
    {
        var runner = new SystemProcessRunner(TimeSpan.FromSeconds(10));

        Assert.That(
            () => runner.Run("/bin/sh", ["-c", "echo 'mon down' >&2; exit 1"]),
            Throws.InvalidOperationException.With.Message.Contains("mon down"));
    }

    [Test]
    public void A_successful_command_that_also_writes_to_stderr_still_returns_stdout()
    {
        var runner = new SystemProcessRunner(TimeSpan.FromSeconds(10));

        var output = runner.Run("/bin/sh", ["-c", "echo warn >&2; echo '{\"status\":\"HEALTH_OK\"}'"]);

        Assert.That(output, Does.Contain("HEALTH_OK"));
    }

    /// <summary>
    /// The regression: reading stdout to completion before touching stderr deadlocks as soon as
    /// the child fills the unread stderr pipe buffer, which is about 64 KiB on Linux. `ceph`
    /// writes to stderr on warnings and auth problems.
    /// </summary>
    [Test]
    public void A_child_flooding_stderr_does_not_deadlock()
    {
        var runner = new SystemProcessRunner(TimeSpan.FromSeconds(20));

        var output = Bounded.Within(
            TimeSpan.FromSeconds(30),
            () => runner.Run("/bin/sh",
            [
                "-c",
                "for i in $(seq 1 4000); do echo 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' >&2; done; echo done"
            ]),
            "a command whose child floods stderr");

        Assert.That(output.Trim(), Is.EqualTo("done"));
    }

    [Test]
    public void A_child_flooding_stdout_returns_all_of_it()
    {
        var runner = new SystemProcessRunner(TimeSpan.FromSeconds(20));

        var output = Bounded.Within(
            TimeSpan.FromSeconds(30),
            () => runner.Run("/bin/sh", ["-c", "for i in $(seq 1 5000); do echo line$i; done"]),
            "a command whose child floods stdout");

        Assert.That(output.Split('\n', StringSplitOptions.RemoveEmptyEntries), Has.Length.EqualTo(5000));
    }

    /// <summary>
    /// Ceph access is serialised behind one semaphore, so an unbounded wait meant a single hung
    /// invocation stalled every later query in the process for good.
    /// </summary>
    [Test]
    public void A_hanging_child_times_out()
    {
        var runner = new SystemProcessRunner(TimeSpan.FromMilliseconds(400));
        var stopwatch = Stopwatch.StartNew();

        Assert.That(() => runner.Run("/bin/sh", ["-c", "sleep 30"]), Throws.TypeOf<TimeoutException>());

        stopwatch.Stop();
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
    }

    [Test]
    public void A_timed_out_child_is_killed_rather_than_left_running()
    {
        var runner = new SystemProcessRunner(TimeSpan.FromMilliseconds(400));
        var marker = Path.Join(Path.GetTempPath(), $"coldceph-kill-{Guid.NewGuid():N}");

        Assert.That(
            () => runner.Run("/bin/sh", ["-c", $"sleep 3; touch {marker}"]),
            Throws.TypeOf<TimeoutException>());

        // If the child survived the timeout it would create the marker a couple of seconds later.
        Thread.Sleep(TimeSpan.FromSeconds(4));
        Assert.That(File.Exists(marker), Is.False, "the timed-out child kept running");
    }

    /// <summary>A child that reads stdin must not block forever waiting for input.</summary>
    [Test]
    public void A_child_reading_stdin_is_not_left_waiting()
    {
        var runner = new SystemProcessRunner(TimeSpan.FromSeconds(10));

        var output = Bounded.Within(
            TimeSpan.FromSeconds(15),
            () => runner.Run("/bin/sh", ["-c", "cat; echo finished"]),
            "a command whose child reads stdin");

        Assert.That(output, Does.Contain("finished"));
    }

    [Test]
    public void A_missing_program_throws_rather_than_returning_empty()
    {
        var runner = new SystemProcessRunner(TimeSpan.FromSeconds(10));

        Assert.That(() => runner.Run("/nonexistent/ceph", ["status"]), Throws.Exception);
    }

    [Test]
    public void Arguments_are_passed_without_shell_interpretation()
    {
        var runner = new SystemProcessRunner(TimeSpan.FromSeconds(10));

        // If arguments went through a shell, the semicolon would start a second command.
        var output = runner.Run("/bin/echo", ["a; rm -rf /tmp/nope", "b"]);

        Assert.That(output.Trim(), Is.EqualTo("a; rm -rf /tmp/nope b"));
    }
}

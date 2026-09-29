using System.Diagnostics;
using System.Text;

namespace ColdCeph.Control.Shared;

/// <summary>
/// Runs a child process and returns its stdout, or throws.
/// <para>
/// Two things here are load-bearing. First, stdout and stderr are drained concurrently: reading
/// one to completion before starting on the other deadlocks as soon as the child fills the
/// unread pipe's buffer, and <c>ceph</c> writes to stderr on warnings and auth problems.
/// Second, the wait is bounded and the tree is killed on expiry — the <c>ceph</c> CLI blocks for
/// a long time against an unreachable monitor, and because Ceph access is serialised one hung
/// call would otherwise stall every later query in the process indefinitely.
/// </para>
/// </summary>
public sealed class SystemProcessRunner : IProcessRunner
{
    private readonly TimeSpan _timeout;

    public SystemProcessRunner()
        : this(TimeSpan.FromSeconds(30))
    {
    }

    public SystemProcessRunner(TimeSpan timeout)
    {
        _timeout = timeout > TimeSpan.Zero ? timeout : TimeSpan.FromSeconds(30);
    }

    public string Run(string fileName, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Failed to start {fileName}.");

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var drained = new CountdownEvent(2);
        process.OutputDataReceived += (_, args) => Collect(stdout, drained, args.Data);
        process.ErrorDataReceived += (_, args) => Collect(stderr, drained, args.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // A child that inherits an open stdin can block forever waiting on it.
        process.StandardInput.Close();

        if (!process.WaitForExit(_timeout))
        {
            Kill(process);
            throw new TimeoutException(
                $"{fileName} did not exit within {_timeout.TotalSeconds:0.#}s and was killed.");
        }

        // WaitForExit(timeout) does not guarantee the async readers finished; the parameterless
        // overload does, and the process has already exited by here.
        drained.Wait(TimeSpan.FromSeconds(5));

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{fileName} exited {process.ExitCode}: {stderr.ToString().Trim()}");

        return stdout.ToString();
    }

    private static void Collect(StringBuilder target, CountdownEvent drained, string? line)
    {
        if (line is null)
        {
            if (!drained.IsSet)
                drained.Signal();
            return;
        }

        lock (target)
            target.Append(line).Append('\n');
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Already gone, or we may not signal it. Either way the timeout is what the caller
            // needs to hear about.
        }
    }
}

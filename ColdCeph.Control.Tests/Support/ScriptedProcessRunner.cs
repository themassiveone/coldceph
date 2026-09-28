using ColdCeph.Control.Shared;

namespace ColdCeph.Control.Tests.Support;

/// <summary>
/// Answers each command with its own output, so a test cannot accidentally feed
/// <c>health detail</c> JSON to the <c>quorum_status</c> or <c>pg stat</c> parser. Also models
/// the two failures a real <c>ceph</c> invocation has: a non-zero exit with stderr, and empty
/// output.
/// <para>
/// A command with no scripted answer throws rather than returning a default, so a provider
/// that starts issuing an extra command fails the test instead of silently reading someone
/// else's JSON.
/// </para>
/// </summary>
public sealed class ScriptedProcessRunner : IProcessRunner
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Func<string>> _answers = new(StringComparer.Ordinal);

    public List<string> Commands { get; } = [];

    /// <summary>Matches on the ceph subcommand, for example "health detail" or "pg stat".</summary>
    public ScriptedProcessRunner Answer(string subcommand, string output)
    {
        _answers[Normalise(subcommand)] = () => output;
        return this;
    }

    public ScriptedProcessRunner Fail(string subcommand, string stderr, int exitCode = 1)
    {
        _answers[Normalise(subcommand)] = () =>
            throw new InvalidOperationException($"ceph exited {exitCode}: {stderr}");
        return this;
    }

    public ScriptedProcessRunner Hang(string subcommand)
    {
        _answers[Normalise(subcommand)] = () =>
            throw new TimeoutException("ceph did not exit within the timeout.");
        return this;
    }

    public int CountOf(string subcommand)
    {
        var needle = Normalise(subcommand);
        lock (_gate)
            return Commands.Count(command => Normalise(command).Contains(needle, StringComparison.Ordinal));
    }

    public string Run(string fileName, IReadOnlyList<string> arguments)
    {
        var command = $"{fileName} {string.Join(' ', arguments)}";
        Func<string>? answer;
        lock (_gate)
        {
            Commands.Add(command);
            answer = _answers
                .Where(pair => Normalise(command).Contains(pair.Key, StringComparison.Ordinal))
                .OrderByDescending(pair => pair.Key.Length)
                .Select(pair => pair.Value)
                .FirstOrDefault();
        }

        if (answer is null)
            throw new InvalidOperationException(
                $"No scripted answer for '{command}'. Script it, or stop issuing it.");

        return answer();
    }

    private static string Normalise(string value) => value.Replace('-', ' ');
}

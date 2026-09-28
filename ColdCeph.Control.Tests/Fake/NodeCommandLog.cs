namespace ColdCeph.Control.Tests.Fake;

/// <summary>
/// One ordered record of every command Control sent to a node, across both the OSD and the disk
/// client. Ordering across those two is the whole point: an OSD must not be started before its
/// disk is awake, and two separately-tested fakes could never show that.
/// </summary>
public sealed class NodeCommandLog
{
    private readonly object _gate = new();
    private readonly List<string> _commands = [];

    public IReadOnlyList<string> Commands
    {
        get
        {
            lock (_gate)
                return _commands.ToArray();
        }
    }

    public void Add(string command)
    {
        lock (_gate)
            _commands.Add(command);
    }

    public int CountOf(string command)
        => Commands.Count(entry => entry.Contains(command, StringComparison.Ordinal));

    public int IndexOf(string command)
    {
        var commands = Commands;
        for (var index = 0; index < commands.Count; index++)
            if (commands[index].Contains(command, StringComparison.Ordinal))
                return index;
        return -1;
    }
}

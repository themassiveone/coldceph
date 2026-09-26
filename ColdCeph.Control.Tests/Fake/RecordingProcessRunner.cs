using ColdCeph.Control.Shared;

namespace ColdCeph.Control.Tests.Fake;

public sealed class RecordingProcessRunner : IProcessRunner
{
    private readonly object _gate = new();
    public List<string> Commands { get; } = [];
    public string Output { get; set; } = """{"status":"HEALTH_OK"}""";

    public string Run(string fileName, IReadOnlyList<string> arguments)
    {
        lock (_gate)
            Commands.Add($"{fileName} {string.Join(' ', arguments)}");
        return Output;
    }
}

using ColdCeph.Control.Shared;

namespace ColdCeph.Control.Tests.Fake;

public sealed class RecordingProcessRunner : IProcessRunner
{
    public List<string> Commands { get; } = [];
    public string Output { get; set; } = """{"status":"HEALTH_OK"}""";

    public string Run(string fileName, IReadOnlyList<string> arguments)
    {
        Commands.Add($"{fileName} {string.Join(' ', arguments)}");
        return Output;
    }
}

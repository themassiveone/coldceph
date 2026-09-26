namespace ColdCeph.Agent.Composition;

public sealed class AgentConfig
{
    public int Port { get; init; } = 7080;
    public string HostId { get; init; } = Environment.MachineName;
    public string Hostname { get; init; } = Environment.MachineName;
    public string AgentToken { get; init; } = "changeme";
    public string DataDirectory { get; init; } = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "coldceph-agent");

    public static AgentConfig FromEnvironment()
    {
        var port = Environment.GetEnvironmentVariable("AGENT_PORT");
        var token = Environment.GetEnvironmentVariable("COLDCEPH_AGENT_TOKEN") ?? "changeme";
        var data = Environment.GetEnvironmentVariable("COLDCEPH_AGENT_DATA");
        return new AgentConfig
        {
            Port = port is not null ? int.Parse(port) : 7080,
            AgentToken = token,
            DataDirectory = data ?? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "coldceph-agent")
        };
    }
}

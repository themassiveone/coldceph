namespace ColdCeph.Agent.Composition;

public sealed class AgentConfig
{
    public int Port { get; init; } = 7080;
    public string HostId { get; init; } = Environment.MachineName;
    public string Hostname { get; init; } = Environment.MachineName;
    public string AgentToken { get; init; } = "changeme";
    public string DataDirectory { get; init; } = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "coldceph-agent");
    public Uri? ControlEndpoint { get; init; }
    public Uri AdvertiseEndpoint { get; init; } = new("http://127.0.0.1:7080");

    public IReadOnlyList<string> ListenUrls() => [$"http://*:{Port}"];

    public static AgentConfig FromEnvironment()
    {
        var port = Environment.GetEnvironmentVariable("AGENT_PORT");
        var token = Optional("COLDCEPH_AGENT_TOKEN") ?? "changeme";
        var data = Optional("COLDCEPH_AGENT_DATA");
        var hostId = Optional("COLDCEPH_HOST_ID") ?? Environment.MachineName;
        var hostname = Optional("COLDCEPH_HOSTNAME") ?? hostId;
        var listenPort = port is not null ? int.Parse(port) : 7080;
        var advertise = Optional("COLDCEPH_ADVERTISE_URL") ?? $"http://127.0.0.1:{listenPort}";
        var control = Optional("COLDCEPH_CONTROL_ENDPOINT");
        return new AgentConfig
        {
            Port = listenPort,
            HostId = hostId,
            Hostname = hostname,
            AgentToken = token,
            DataDirectory = data ?? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "coldceph-agent"),
            ControlEndpoint = control is null ? null : new Uri(control, UriKind.Absolute),
            AdvertiseEndpoint = new Uri(advertise, UriKind.Absolute)
        };
    }

    private static string? Optional(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}

using ColdCeph.Core.Features.S3.DTOs;

namespace ColdCeph.Control.Composition;

public sealed class ControlConfig
{
    public int OperatorPort { get; init; } = 8080;
    public int S3Port { get; init; } = 7480;
    public Uri RgwEndpoint { get; init; } = new("http://127.0.0.1:7481");
    public string DataDirectory { get; init; } = "/var/lib/coldceph";
    public string OperatorPassword { get; init; } = "changeme";
    public string AgentToken { get; init; } = "changeme";
    public string ControllerIdentity { get; init; } = "coldceph-control";
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(15);
    public S3AdmissionMode S3Mode { get; init; } = S3AdmissionMode.Wait;
    public int RetryAfterSeconds { get; init; } = 30;
    public string CephBinary { get; init; } = "ceph";
    public string? CephConf { get; init; }
    public string? CephKeyring { get; init; }
    public IReadOnlyList<Uri> ConfiguredAgentEndpoints { get; init; } = [];
    public string ConfiguredAgentHostId { get; init; } = "dev";
    public TimeSpan HeartbeatStaleAfter { get; init; } = TimeSpan.FromSeconds(45);
    public bool BindHttpListeners { get; init; } = true;
    public TimeSpan CephQueryCacheTtl { get; init; } = TimeSpan.FromSeconds(2);

    public IReadOnlyList<string> ListenUrls()
        => [$"http://*:{OperatorPort}", $"http://*:{S3Port}"];

    public IReadOnlyList<string> BuildCephArguments(params string[] command)
    {
        var arguments = new List<string>();
        if (!string.IsNullOrWhiteSpace(CephConf))
        {
            arguments.Add("--conf");
            arguments.Add(CephConf);
        }

        if (!string.IsNullOrWhiteSpace(CephKeyring))
        {
            arguments.Add("--keyring");
            arguments.Add(CephKeyring);
        }

        arguments.AddRange(command);
        return arguments;
    }

    public static ControlConfig FromEnvironment()
    {
        var webPort = Environment.GetEnvironmentVariable("WEB_PORT");
        var s3Port = Environment.GetEnvironmentVariable("S3_PORT");
        var dataDirectory = Environment.GetEnvironmentVariable("COLDCEPH_DATA") ?? DefaultDataDirectory();
        var password = Optional("COLDCEPH_OPERATOR_PASSWORD") ?? "changeme";
        var token = Optional("COLDCEPH_AGENT_TOKEN") ?? "changeme";
        var rgw = Optional("COLDCEPH_RGW") ?? "http://127.0.0.1:7481";
        var idle = Environment.GetEnvironmentVariable("COLDCEPH_IDLE_SECONDS");
        var mode = Environment.GetEnvironmentVariable("COLDCEPH_S3_MODE");
        var cephBinary = Optional("COLDCEPH_CEPH_BINARY") ?? "ceph";
        var hostId = Optional("COLDCEPH_AGENT_HOST_ID") ?? "dev";

        return new ControlConfig
        {
            OperatorPort = webPort is not null ? int.Parse(webPort) : 8080,
            S3Port = s3Port is not null ? int.Parse(s3Port) : 7480,
            RgwEndpoint = new Uri(rgw),
            DataDirectory = dataDirectory,
            OperatorPassword = password,
            AgentToken = token,
            IdleTimeout = idle is not null ? TimeSpan.FromSeconds(int.Parse(idle)) : TimeSpan.FromMinutes(15),
            S3Mode = string.Equals(mode, "retry", StringComparison.OrdinalIgnoreCase)
                ? S3AdmissionMode.Retry
                : S3AdmissionMode.Wait,
            CephBinary = cephBinary,
            CephConf = Optional("COLDCEPH_CEPH_CONF"),
            CephKeyring = Optional("COLDCEPH_CEPH_KEYRING"),
            ConfiguredAgentEndpoints = ParseEndpoints(Optional("COLDCEPH_AGENT_ENDPOINT")),
            ConfiguredAgentHostId = hostId,
            BindHttpListeners = Environment.GetEnvironmentVariable("COLDCEPH_BIND") != "0"
        };
    }

    private static string? Optional(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static IReadOnlyList<Uri> ParseEndpoints(string? value)
    {
        if (value is null)
            return [];

        return value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => new Uri(entry, UriKind.Absolute))
            .ToArray();
    }

    private static string DefaultDataDirectory()
    {
        var local = Environment.GetEnvironmentVariable("HOME");
        return string.IsNullOrWhiteSpace(local)
            ? "/var/lib/coldceph"
            : Path.Join(local, ".local", "share", "coldceph");
    }
}

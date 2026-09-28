using ColdCeph.Core.Features.S3.DTOs;

namespace ColdCeph.Control.Composition;

public sealed class ControlConfig
{
    public int OperatorPort { get; init; } = 8080;
    public int S3Port { get; init; } = 7480;
    public Uri RgwEndpoint { get; init; } = new("http://127.0.0.1:7481");
    public string DataDirectory { get; init; } = "/var/lib/coldceph";
    public string OperatorPassword { get; init; } = "changeme";
    public string NodeToken { get; init; } = "changeme";
    public string ControllerIdentity { get; init; } = "coldceph-control";
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(15);
    public S3AdmissionMode S3Mode { get; init; } = S3AdmissionMode.Wait;
    public int RetryAfterSeconds { get; init; } = 30;
    public string CephBinary { get; init; } = "ceph";
    public string? CephContainer { get; init; }
    public string? CephConf { get; init; }
    public string? CephKeyring { get; init; }
    public IReadOnlyList<Uri> ConfiguredNodeEndpoints { get; init; } = [];
    public string ConfiguredNodeHostId { get; init; } = "dev";
    public TimeSpan HeartbeatStaleAfter { get; init; } = TimeSpan.FromSeconds(45);
    public bool BindHttpListeners { get; init; } = true;

    /// <summary>
    /// Whether the three reconcile loops run. Separate from <see cref="BindHttpListeners"/> so a
    /// test can bind both real listeners — which is the only way to exercise port-based S3
    /// dispatch — without a background loop moving the plane underneath it.
    /// </summary>
    public bool RunReconcilers { get; init; } = true;
    /// <summary>
    /// How long a single ceph invocation may run before it is killed. Without a bound, one
    /// hung call blocks every other Ceph query in the process, because they are serialised.
    /// </summary>
    public TimeSpan CephCommandTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How long to wait for a node to answer a start/stop/wake/standby command.</summary>
    public TimeSpan NodeCommandTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public string S3AccessKey { get; init; } = "coldceph";
    public string S3SecretKey { get; init; } = "coldcephsecret";
    public string S3Region { get; init; } = "us-east-1";

    public IReadOnlyList<string> ListenUrls()
        => [$"http://*:{OperatorPort}", $"http://*:{S3Port}"];

    public (string FileName, IReadOnlyList<string> Arguments) InvokeCeph(IReadOnlyList<string> command)
    {
        if (!string.IsNullOrWhiteSpace(CephContainer))
            return ("docker", ["exec", CephContainer, "ceph", ..command]);

        return (RequireCephProgram(CephBinary), BuildCephArguments(command));
    }

    public IReadOnlyList<string> BuildCephArguments(IReadOnlyList<string> command)
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
        var token = Optional("COLDCEPH_NODE_TOKEN") ?? "changeme";
        var rgw = Optional("COLDCEPH_RGW") ?? "http://127.0.0.1:7481";
        var idle = Environment.GetEnvironmentVariable("COLDCEPH_IDLE_SECONDS");
        var mode = Environment.GetEnvironmentVariable("COLDCEPH_S3_MODE");
        var cephBinary = Optional("COLDCEPH_CEPH_BINARY") ?? "ceph";
        var hostId = Optional("COLDCEPH_NODE_HOST_ID") ?? "dev";

        return new ControlConfig
        {
            OperatorPort = webPort is not null ? int.Parse(webPort) : 8080,
            S3Port = s3Port is not null ? int.Parse(s3Port) : 7480,
            RgwEndpoint = new Uri(rgw),
            DataDirectory = dataDirectory,
            OperatorPassword = password,
            NodeToken = token,
            IdleTimeout = idle is not null ? TimeSpan.FromSeconds(int.Parse(idle)) : TimeSpan.FromMinutes(15),
            S3Mode = string.Equals(mode, "retry", StringComparison.OrdinalIgnoreCase)
                ? S3AdmissionMode.Retry
                : S3AdmissionMode.Wait,
            CephBinary = RequireCephProgram(cephBinary),
            CephContainer = Optional("COLDCEPH_CEPH_CONTAINER"),
            CephConf = Optional("COLDCEPH_CEPH_CONF"),
            CephKeyring = Optional("COLDCEPH_CEPH_KEYRING"),
            ConfiguredNodeEndpoints = ParseEndpoints(Optional("COLDCEPH_NODE_ENDPOINT")),
            ConfiguredNodeHostId = hostId,
            BindHttpListeners = Environment.GetEnvironmentVariable("COLDCEPH_BIND") != "0",
            RunReconcilers = Environment.GetEnvironmentVariable("COLDCEPH_BIND") != "0",
            S3AccessKey = Optional("COLDCEPH_S3_ACCESS_KEY") ?? "coldceph",
            S3SecretKey = Optional("COLDCEPH_S3_SECRET_KEY") ?? "coldcephsecret",
            S3Region = Optional("COLDCEPH_S3_REGION") ?? "us-east-1"
        };
    }

    internal static string RequireCephProgram(string configured)
    {
        var fileName = Path.GetFileName(configured);
        if (!string.Equals(fileName, "ceph", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"COLDCEPH_CEPH_BINARY must be the ceph program, not '{configured}'.");

        if (configured.Contains(Path.DirectorySeparatorChar)
            || configured.Contains(Path.AltDirectorySeparatorChar)
            || configured.Contains('/'))
        {
            if (!Path.IsPathRooted(configured))
                throw new InvalidOperationException(
                    "COLDCEPH_CEPH_BINARY must be the program name 'ceph' or an absolute path to ceph, not a relative script.");
        }

        return configured;
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

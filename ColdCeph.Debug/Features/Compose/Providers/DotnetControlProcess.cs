using System.Diagnostics;
using ColdCeph.Debug.Features.Compose.Interfaces;
using ColdCeph.Debug.Shared;

namespace ColdCeph.Debug.Features.Compose.Providers;

public sealed class DotnetControlProcess : IControlProcess
{
    private readonly IControlHealth _health;

    public DotnetControlProcess(IControlHealth health)
    {
        _health = health;
    }

    public bool Owned => TryReadPid() is { } pid && IsAlive(pid);

    public void Start()
    {
        var root = RepositoryRoot.Find();
        Directory.CreateDirectory(Path.GetDirectoryName(PidPath(root))!);
        var logPath = Path.Join(root, ".git", "coldceph", "debug", "control.log");
        var start = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add("run");
        start.ArgumentList.Add("--project");
        start.ArgumentList.Add("ColdCeph.Control");
        start.ArgumentList.Add("--no-launch-profile");
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["WEB_PORT"] = Environment.GetEnvironmentVariable("WEB_PORT") ?? "8080";
        start.Environment["S3_PORT"] = Environment.GetEnvironmentVariable("S3_PORT") ?? "7480";
        start.Environment["COLDCEPH_RGW"] = Environment.GetEnvironmentVariable("COLDCEPH_RGW") ?? "http://127.0.0.1:7481";
        start.Environment["COLDCEPH_CEPH_BINARY"] = "ceph";
        start.Environment["COLDCEPH_CEPH_CONTAINER"] =
            Environment.GetEnvironmentVariable("COLDCEPH_CEPH_CONTAINER") ?? "coldceph-mon";
        start.Environment["COLDCEPH_OPERATOR_PASSWORD"] =
            Environment.GetEnvironmentVariable("COLDCEPH_OPERATOR_PASSWORD") ?? "changeme";
        start.Environment["COLDCEPH_NODE_TOKEN"] = Environment.GetEnvironmentVariable("COLDCEPH_NODE_TOKEN") ?? "changeme";

        var process = Process.Start(start)
            ?? throw new InvalidOperationException("Failed to start ColdCeph.Control.");
        File.WriteAllText(PidPath(root), process.Id.ToString());
        DrainToFile(process, logPath);
        WaitUntilReady();
    }

    public void Stop()
    {
        var pid = TryReadPid();
        if (pid is { } id && IsAlive(id))
        {
            try
            {
                using var process = Process.GetProcessById(id);
                process.Kill(entireProcessTree: true);
                process.WaitForExit(TimeSpan.FromSeconds(10));
            }
            catch (ArgumentException)
            {
            }
        }

        var path = PidPath(RepositoryRoot.Find());
        if (File.Exists(path))
            File.Delete(path);
    }

    private void WaitUntilReady()
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if (_health.IsReady())
                return;
            Thread.Sleep(500);
        }

        throw new InvalidOperationException("Control did not become ready on /health.");
    }

    private static void DrainToFile(Process process, string logPath)
    {
        _ = Task.Run(async () =>
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(stdoutTask, stderrTask);
            await File.AppendAllTextAsync(logPath, await stdoutTask + await stderrTask);
        });
    }

    private static int? TryReadPid()
    {
        var path = PidPath(RepositoryRoot.Find());
        if (!File.Exists(path))
            return null;
        return int.TryParse(File.ReadAllText(path).Trim(), out var pid) ? pid : null;
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string PidPath(string root)
        => Path.Join(root, ".git", "coldceph", "debug", "control.pid");
}

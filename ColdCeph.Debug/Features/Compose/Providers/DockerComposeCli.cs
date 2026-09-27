using System.Diagnostics;
using ColdCeph.Debug.Features.Compose.Interfaces;
using ColdCeph.Debug.Shared;

namespace ColdCeph.Debug.Features.Compose.Providers;

public sealed class DockerComposeCli : IComposeCli
{
    public string Run(params string[] arguments)
    {
        var root = RepositoryRoot.Find();
        var start = new ProcessStartInfo
        {
            FileName = "docker",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("compose");
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Failed to start docker compose.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"docker compose exited {process.ExitCode}: {stderr}");
        return stdout;
    }
}

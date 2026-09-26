using System.Diagnostics;
using ColdCeph.E2E.Tests.States;
using Xcepto.Adapters;

namespace ColdCeph.E2E.Tests.Adapters;

public sealed class CephAdapter : XceptoAdapter
{
    private readonly string _binary;
    private readonly string _workingDirectory;

    internal CephAdapter(string binary, string workingDirectory)
    {
        _binary = binary;
        _workingDirectory = workingDirectory;
    }

    public void SeeQuorum()
    {
        AddStep(new ExpectationStepState("Ceph monitor quorum", async () =>
        {
            var output = await Run("quorum_status", "--format", "json");
            return output.Contains("quorum", StringComparison.OrdinalIgnoreCase);
        }));
    }

    public void SeeHealthNotSilent()
    {
        AddStep(new ExpectationStepState("Ceph reports a health status", async () =>
        {
            var output = await Run("status", "--format", "json");
            return output.Contains("HEALTH_OK", StringComparison.OrdinalIgnoreCase)
                   || output.Contains("HEALTH_WARN", StringComparison.OrdinalIgnoreCase)
                   || output.Contains("HEALTH_ERR", StringComparison.OrdinalIgnoreCase);
        }));
    }

    public void SeeOsdMap()
    {
        AddStep(new ExpectationStepState("Ceph has an OSD map", async () =>
        {
            var output = await Run("osd", "stat", "--format", "json");
            return output.Contains("num_osds", StringComparison.OrdinalIgnoreCase)
                   || output.Contains("osd", StringComparison.OrdinalIgnoreCase);
        }));
    }

    private async Task<string> Run(params string[] arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = _binary,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _workingDirectory
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start ceph wrapper.");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ceph exited {process.ExitCode}: {stderr}");
        return stdout;
    }
}

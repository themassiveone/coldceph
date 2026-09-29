using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Integrity.Providers;
using ColdCeph.Core.Features.Integrity.DTOs;

namespace ColdCeph.Control.Tests.Support;

/// <summary>
/// Loads a scenario from <c>Support/CephFixtures</c> and scripts a runner with it, so provider
/// tests run against output shaped like Ceph's rather than strings written in the test.
/// See that folder's README for provenance and for how to add a scenario.
/// </summary>
public static class CephFixture
{
    public const string Healthy = "healthy";
    public const string DemoWarnings = "demo-warnings";
    public const string ColdOsdsDown = "cold-osds-down";
    public const string WakingPeering = "waking-peering";
    public const string ScopedNoout = "scoped-noout";
    public const string ClusterNoout = "cluster-noout";
    public const string OtherFlag = "other-flag";
    public const string Unfound = "unfound";
    public const string Inconsistent = "inconsistent";
    public const string Incomplete = "incomplete";
    public const string NearFull = "nearfull";
    public const string Recovering = "recovering";
    public const string NoQuorum = "no-quorum";

    public static readonly string[] All =
    [
        Healthy, DemoWarnings, ColdOsdsDown, WakingPeering, ScopedNoout, ClusterNoout,
        OtherFlag, Unfound, Inconsistent, Incomplete, NearFull, Recovering, NoQuorum
    ];

    private static readonly (string Subcommand, string File)[] Commands =
    [
        ("health detail", "health-detail.json"),
        ("quorum_status", "quorum_status.json"),
        ("status", "status.json"),
        ("osd dump", "osd-dump.json")
    ];

    public static ScriptedProcessRunner Runner(string scenario)
    {
        var runner = new ScriptedProcessRunner();
        foreach (var (subcommand, file) in Commands)
            runner.Answer(subcommand, Read(scenario, file));
        return runner;
    }

    public static CephCliQueryProvider Provider(string scenario, out ScriptedProcessRunner runner)
    {
        runner = Runner(scenario);
        return new CephCliQueryProvider(new ControlConfig { CephBinary = "ceph" }, runner);
    }

    public static CephCliQueryProvider Provider(string scenario) => Provider(scenario, out _);

    /// <summary>
    /// The observation the real parser produces for a scenario. Tests that want a cluster
    /// state take it from here rather than hand-writing health strings.
    /// </summary>
    public static CephObservation Observation(string scenario) => Provider(scenario).GetObservation();

    public static CephSignals Signals(string scenario) => CephSignals.From(Observation(scenario));

    public static string Read(string scenario, string file)
    {
        var path = Path.Join(Root(), scenario, file);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Ceph fixture '{scenario}/{file}' is missing. Add it under Support/CephFixtures.", path);
        return File.ReadAllText(path);
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Join(directory.FullName, "Support", "CephFixtures");
            if (Directory.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate Support/CephFixtures.");
    }
}

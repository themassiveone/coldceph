using ColdCeph.Control.Tests.Support;

namespace ColdCeph.Control.Tests.Features.Integrity.HTTP;

/// <summary>
/// Pins the wire shape of the integrity document that <c>ColdCeph.E2E.Tests</c> asserts against.
/// <para>
/// The E2E operator adapter reads this payload as text — it has no reference to the DTOs — so a
/// rename or a change of enum representation would make its assertions silently stop matching and
/// the journey would pass while checking nothing. This fails here instead.
/// </para>
/// </summary>
[TestFixture]
public sealed class IntegrityWireShapeTests
{
    [Test]
    public async Task The_integrity_document_carries_the_fields_the_e2e_adapter_reads()
    {
        using var factory = new ControlAppFactory();
        factory.Ceph.Seeing(CephFixture.ColdOsdsDown);
        using var client = await OperatorClient.SignedIn(factory);

        var json = await client.GetStringAsync("/v1/cluster/integrity");

        Assert.That(json, Does.Contain("\"durabilityFailure\""), "SeeLiveHealthIsNotADurabilityFailure reads this");
        Assert.That(json, Does.Contain("\"name\""), "UnexpectedChecks matches on this");
        Assert.That(json, Does.Contain("\"classification\""), "UnexpectedChecks matches on this");
        Assert.That(json, Does.Contain("\"status\""), "ConfirmIntegrityAsync checks this for UNAVAILABLE");
        Assert.That(json, Does.Contain("OSD_DOWN"), "check names reach the wire unaltered");
    }

    /// <summary>
    /// A clean confirmation says so in the exact form the adapter looks for, with no space after the
    /// colon — the adapter tolerates both, but this pins which one is actually produced.
    /// </summary>
    [Test]
    public async Task A_clean_confirmation_reports_durability_failure_false()
    {
        using var factory = new ControlAppFactory();
        using var client = await OperatorClient.SignedIn(factory);

        var json = await client.GetStringAsync("/v1/cluster/integrity");

        Assert.That(json, Does.Contain("\"durabilityFailure\":false"));
    }

    [Test]
    public async Task A_durability_failure_reports_it_as_true()
    {
        using var factory = new ControlAppFactory();
        factory.Ceph.Seeing(CephFixture.Unfound);
        using var client = await OperatorClient.SignedIn(factory);

        var json = await client.GetStringAsync("/v1/cluster/integrity");

        Assert.That(json, Does.Contain("\"durabilityFailure\":true"));
        Assert.That(json, Does.Not.Contain("\"durabilityFailure\":false"));
    }

    /// <summary>
    /// The adapter's regex walks each classified check as a flat object starting at "name". This
    /// confirms an unexpected check really is discoverable that way, and an expected one is not.
    /// </summary>
    [Test]
    public async Task An_unexpected_check_is_discoverable_by_the_adapters_regex()
    {
        using var factory = new ControlAppFactory();
        factory.Ceph.Seeing(CephFixture.ClusterNoout);
        using var client = await OperatorClient.SignedIn(factory);

        var json = await client.GetStringAsync("/v1/cluster/integrity");

        Assert.That(Unexpected(json), Does.Contain("OSDMAP_FLAGS"));
    }

    [Test]
    public async Task An_expected_check_is_not_reported_as_unexpected()
    {
        using var factory = new ControlAppFactory();
        factory.Ceph.Seeing(CephFixture.DemoWarnings);
        using var client = await OperatorClient.SignedIn(factory);

        var json = await client.GetStringAsync("/v1/cluster/integrity");

        Assert.That(Unexpected(json), Is.Empty);
    }

    /// <summary>The same matching the E2E adapter performs, kept in step with it deliberately.</summary>
    private static IReadOnlyList<string> Unexpected(string json)
        => System.Text.RegularExpressions.Regex
            .Matches(json, "\\{\\s*\"name\"\\s*:\\s*\"(?<name>[^\"]+)\"(?<body>.*?)\\}",
                System.Text.RegularExpressions.RegexOptions.Singleline)
            .Where(match => match.Groups["body"].Value.Contains("\"unexpected\"", StringComparison.OrdinalIgnoreCase)
                            || match.Groups["body"].Value.Contains("\"classification\":1", StringComparison.Ordinal)
                            || match.Groups["body"].Value.Contains("\"classification\": 1", StringComparison.Ordinal))
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}

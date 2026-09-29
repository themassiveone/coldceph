using ColdCeph.Architecture.Tests.Fixtures;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ColdCeph.Architecture.Tests.Rules;

/// <summary>
/// Structural rules cannot tell a meaningful test from an empty one, but they can tell that the
/// riskiest code has no test fixture at all.
/// <para>
/// Every one of ColdCeph's worst defects lived in a <c>Providers/</c> type or a <c>Shared/</c>
/// process runner — the adapters to Ceph, to systemd, to hdparm, to RGW, to the node HTTP API.
/// <c>HdparmDiskPower</c>, <c>StreamingRgwProxy</c>, <c>SystemProcessRunner</c> and both node
/// clients had no tests, while the in-memory stand-in for one of them had a fixture of its own.
/// These rules would have said so on the day each was written.
/// </para>
/// </summary>
[TestFixture]
public sealed class ProviderCoverage
{
    /// <summary>
    /// Adapters that are pure plumbing over another adapter, with no parsing or decision of their
    /// own, and the in-memory stand-ins that exist only for tests.
    /// </summary>
    private static readonly string[] Exempt =
    [
        "MemoryDiskPower",
        "NoopControlOsdsReporter",
        "NoopControlDevicesReporter"
    ];

    [Test]
    public void Every_provider_has_a_provider_test_fixture()
    {
        var root = ArchitectureFixture.FindRepositoryRoot(TestContext.CurrentContext.TestDirectory);
        var tested = TestedTypeNames(root);

        var violations = ProviderTypes(root)
            .Where(provider => !Exempt.Contains(provider.Type, StringComparer.Ordinal))
            .Where(provider => !tested.Contains(provider.Type))
            .Select(provider =>
                $"{provider.Location}: {provider.Type} talks to something outside this process and has no test. "
                + $"Add {provider.Project}.Tests/Features/{provider.Slice}/Provider/{provider.Type}Tests.cs")
            .ToArray();

        Assert.That(violations, Is.Empty,
            "A provider is where ColdCeph meets Ceph, systemd, hdparm, RGW or a node. "
            + "Each one needs a fixture that drives it against output the real thing produces.");
    }

    /// <summary>
    /// A fake is allowed to stand in for an external system. It is not allowed to be the only
    /// thing tested: if a production interface has a fake, the real implementation of that
    /// interface needs its own tests, or the suite is testing the stand-in.
    /// </summary>
    [Test]
    public void A_fake_does_not_substitute_for_testing_the_real_implementation()
    {
        var root = ArchitectureFixture.FindRepositoryRoot(TestContext.CurrentContext.TestDirectory);
        var tested = TestedTypeNames(root);
        var providers = ProviderTypes(root).ToArray();

        var violations = FakedInterfaces(root)
            .SelectMany(faked => providers
                .Where(provider => provider.Interfaces.Contains(faked.Interface, StringComparer.Ordinal))
                .Where(provider => !Exempt.Contains(provider.Type, StringComparer.Ordinal))
                .Where(provider => !tested.Contains(provider.Type))
                .Select(provider =>
                    $"{faked.Location}: {faked.Fake} stands in for {faked.Interface}, "
                    + $"but its real implementation {provider.Type} has no tests"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.That(violations, Is.Empty,
            "Testing only the fake means the suite never sees what the real adapter does with real input.");
    }

    /// <summary>
    /// The shared process runners are how Control and Node start every child process. They carry
    /// the timeout and the pipe handling, which are the two ways a loop hangs for good.
    /// </summary>
    [Test]
    public void Every_process_runner_has_tests()
    {
        var root = ArchitectureFixture.FindRepositoryRoot(TestContext.CurrentContext.TestDirectory);
        var tested = TestedTypeNames(root);

        var violations = ArchitectureFixture.ProductionProjects
            .SelectMany(project => ArchitectureFixture.ProjectSourceFiles(root, project)
                .Where(path => ArchitectureFixture.HasPathPart(root, path, "Shared"))
                .SelectMany(path => DeclaredTypes(path)
                    .Where(type => type.Name.EndsWith("ProcessRunner", StringComparison.Ordinal))
                    .Where(type => !tested.Contains(type.Name))
                    .Select(type => $"{ArchitectureFixture.Relative(root, path)}: {type.Name} has no tests")))
            .ToArray();

        Assert.That(violations, Is.Empty,
            "A process runner decides whether a hung child stalls the process. That needs a test.");
    }

    private static HashSet<string> TestedTypeNames(string root)
        => ArchitectureFixture.TestProjects
            .Where(project => Directory.Exists(Path.Join(root, project)))
            .SelectMany(project => ArchitectureFixture.ProjectSourceFiles(root, project))
            .Where(path => Path.GetFileName(path).EndsWith("Tests.cs", StringComparison.Ordinal))
            .Select(path => Path.GetFileName(path)[..^"Tests.cs".Length])
            .ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<ProviderType> ProviderTypes(string root)
        => ArchitectureFixture.ProductionProjects
            .SelectMany(project => ArchitectureFixture.ProjectSourceFiles(root, project)
                .Where(path => ArchitectureFixture.HasPathPart(root, path, "Providers"))
                .SelectMany(path =>
                {
                    var parts = ArchitectureFixture.Parts(root, path);
                    var slice = parts.Length > 2 ? parts[2] : "Unknown";
                    return DeclaredTypes(path).Select(type => new ProviderType(
                        project,
                        slice,
                        type.Name,
                        type.Interfaces,
                        ArchitectureFixture.Relative(root, path)));
                }));

    private static IEnumerable<FakedInterface> FakedInterfaces(string root)
        => ArchitectureFixture.TestProjects
            .Where(project => Directory.Exists(Path.Join(root, project)))
            .SelectMany(project => ArchitectureFixture.ProjectSourceFiles(root, project)
                .Where(path => ArchitectureFixture.HasPathPart(root, path, "Fake"))
                .SelectMany(path => DeclaredTypes(path)
                    .SelectMany(type => type.Interfaces
                        .Where(name => name.StartsWith("I", StringComparison.Ordinal))
                        .Select(name => new FakedInterface(
                            type.Name,
                            name,
                            ArchitectureFixture.Relative(root, path))))));

    private static IEnumerable<(string Name, string[] Interfaces)> DeclaredTypes(string path)
    {
        var (_, node) = ArchitectureFixture.ParseSourceFile(path);
        return node.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Where(declaration => declaration is ClassDeclarationSyntax or RecordDeclarationSyntax)
            .Select(declaration => (
                declaration.Identifier.Text,
                (declaration.BaseList?.Types ?? default)
                    .Select(baseType => ArchitectureFixture.FinalTypeSegment(baseType.Type))
                    .ToArray()));
    }

    private sealed record ProviderType(
        string Project,
        string Slice,
        string Type,
        string[] Interfaces,
        string Location);

    private sealed record FakedInterface(string Fake, string Interface, string Location);
}

using ColdCeph.Architecture.Tests.Fixtures;

namespace ColdCeph.Architecture.Tests.Rules;

[TestFixture]
public sealed class SourceFileLayout
{
    [Test]
    public void Production_source_files_live_under_features_shared_composition_or_entrypoints()
    {
        var root = ArchitectureFixture.FindRepositoryRoot(TestContext.CurrentContext.TestDirectory);
        var violations = ArchitectureFixture.ProductionSourceFiles(root)
            .Where(path => !IsAllowed(root, path))
            .Select(path => ArchitectureFixture.Relative(root, path))
            .ToArray();

        Assert.That(violations, Is.Empty,
            "Production files must live under Features/, Shared/, Composition/, or Program.cs.");
    }

    private static bool IsAllowed(string root, string path)
    {
        var parts = ArchitectureFixture.Parts(root, path);
        return parts.Length == 2 && parts[1] == "Program.cs"
               || parts.Length >= 3 && parts[1] is "Features" or "Shared" or "Composition";
    }
}

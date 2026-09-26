using ColdCeph.Architecture.Tests.Fixtures;

namespace ColdCeph.Architecture.Tests.Rules;

[TestFixture]
public sealed class TestFileLayout
{
    [Test]
    public void Test_projects_use_feature_sliced_test_kind_folders()
    {
        var root = ArchitectureFixture.FindRepositoryRoot(TestContext.CurrentContext.TestDirectory);
        var violations = ArchitectureFixture.TestSourceFiles(root)
            .Where(path => !IsAllowedSupport(root, path))
            .Select(path => (Path: path, Parts: ArchitectureFixture.Parts(root, path)))
            .Where(item => !IsFeatureSlicedTest(item.Parts))
            .Select(item => ArchitectureFixture.Relative(root, item.Path))
            .ToArray();

        Assert.That(violations, Is.Empty,
            "Tests must live under Features/<Slice>/<TestKind>/, Fake/, Support/, E2E/, or Fixtures/.");
    }

    private static bool IsAllowedSupport(string root, string path)
    {
        var parts = ArchitectureFixture.Parts(root, path);
        return parts[0] == "ColdCeph.Architecture.Tests"
               || parts.Length == 2 && parts[1] == "Program.cs"
               || parts.Length >= 2 && ArchitectureFixture.AllowedRootTestSupportFolders.Contains(parts[1]);
    }

    private static bool IsFeatureSlicedTest(string[] parts)
        => parts.Length >= 5
           && parts[1] == "Features"
           && ArchitectureFixture.AllowedTestKindFolders.Contains(parts[3]);
}

using ColdCeph.Architecture.Tests.Fixtures;

namespace ColdCeph.Architecture.Tests.Rules;

[TestFixture]
public sealed class FeatureTypeFolders
{
    [Test]
    public void Feature_source_files_live_in_approved_type_folders()
    {
        var root = ArchitectureFixture.FindRepositoryRoot(TestContext.CurrentContext.TestDirectory);
        var violations = ArchitectureFixture.ProductionSourceFiles(root)
            .Select(path => (Path: path, Parts: ArchitectureFixture.Parts(root, path)))
            .Where(item => ArchitectureFixture.TryFeatureLocation(item.Parts, out _, out _, out var folder)
                           && !ArchitectureFixture.AllowedFeatureTypeFolders.Contains(folder))
            .Select(item =>
            {
                ArchitectureFixture.TryFeatureLocation(item.Parts, out _, out _, out var folder);
                return $"{ArchitectureFixture.Relative(root, item.Path)} uses unsupported feature type folder '{folder}'";
            })
            .ToArray();

        Assert.That(violations, Is.Empty,
            $"Feature files must use only: {string.Join(", ", ArchitectureFixture.AllowedFeatureTypeFolders)}.");
    }
}

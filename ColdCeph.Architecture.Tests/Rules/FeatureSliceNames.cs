using ColdCeph.Architecture.Tests.Fixtures;

namespace ColdCeph.Architecture.Tests.Rules;

[TestFixture]
public sealed class FeatureSliceNames
{
    [Test]
    public void Feature_names_are_the_approved_user_facing_catalog()
    {
        var root = ArchitectureFixture.FindRepositoryRoot(TestContext.CurrentContext.TestDirectory);
        var violations = ArchitectureFixture.ProductionSourceFiles(root)
            .Select(path => (Path: path, Parts: ArchitectureFixture.Parts(root, path)))
            .Where(item => ArchitectureFixture.TryFeatureLocation(item.Parts, out _, out _, out _))
            .Select(item =>
            {
                ArchitectureFixture.TryFeatureLocation(item.Parts, out var project, out var slice, out _);
                var allowed = project switch
                {
                    "ColdCeph.Control" => ArchitectureFixture.AllowedControlSlices,
                    "ColdCeph.Agent" => ArchitectureFixture.AllowedAgentSlices,
                    "ColdCeph.Core" => ArchitectureFixture.AllowedCoreSlices,
                    _ => []
                };
                return allowed.Contains(slice, StringComparer.Ordinal)
                    ? null
                    : $"{ArchitectureFixture.Relative(root, item.Path)} uses unlisted slice '{slice}'";
            })
            .Where(violation => violation is not null)
            .Distinct()
            .ToArray();

        Assert.That(violations, Is.Empty,
            "Slices must be the user-facing catalog in AGENTS.md.");
    }

    [Test]
    public void Dump_slice_names_are_not_used()
    {
        var root = ArchitectureFixture.FindRepositoryRoot(TestContext.CurrentContext.TestDirectory);
        var violations = ArchitectureFixture.ProductionSourceFiles(root)
            .Select(path => (Path: path, Parts: ArchitectureFixture.Parts(root, path)))
            .Where(item => ArchitectureFixture.TryFeatureLocation(item.Parts, out _, out var slice, out _)
                           && ArchitectureFixture.ForbiddenSliceNames.Contains(slice, StringComparer.Ordinal))
            .Select(item => ArchitectureFixture.Relative(root, item.Path))
            .ToArray();

        Assert.That(violations, Is.Empty,
            "There is no Dashboard, Ceph, ClusterState, or Agents dump slice.");
    }
}

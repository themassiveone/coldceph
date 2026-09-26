using ColdCeph.Architecture.Tests.Fixtures;
using ArchUnitNET.Fluent;
using ArchUnitNET.NUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace ColdCeph.Architecture.Tests.Rules;

[TestFixture]
public sealed class ProjectDependencies
{
    [Test]
    public void Production_project_dependencies_follow_ownership_boundaries()
    {
        AssertDoesNotDependOn("ColdCeph.Core", ["ColdCeph.Core"]);
        AssertDoesNotDependOn("ColdCeph.Agent", ["ColdCeph.Agent", "ColdCeph.Core"]);
        AssertDoesNotDependOn("ColdCeph.Control", ["ColdCeph.Control", "ColdCeph.Core"]);
    }

    private static void AssertDoesNotDependOn(string sourceAssembly, IReadOnlyCollection<string> allowedAssemblies)
    {
        var source = Types().That().ResideInAssembly(sourceAssembly).As(sourceAssembly);
        foreach (var forbiddenAssembly in ArchitectureFixture.ProductionProjects.Except(allowedAssemblies))
        {
            var forbidden = Types().That().ResideInAssembly(forbiddenAssembly).As(forbiddenAssembly);
            IArchRule rule = Types().That().Are(source).Should().NotDependOnAny(forbidden)
                .Because($"{sourceAssembly} must not take runtime dependencies on {forbiddenAssembly}")
                .WithoutRequiringPositiveResults();
            rule.Check(ArchitectureFixture.ArchUnitArchitecture);
        }
    }
}

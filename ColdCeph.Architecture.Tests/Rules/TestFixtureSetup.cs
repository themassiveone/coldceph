using ColdCeph.Architecture.Tests.Fixtures;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ColdCeph.Architecture.Tests.Rules;

[TestFixture]
public sealed class TestFixtureSetup
{
    private static readonly string[] SetUpAttributes = ["SetUp", "OneTimeSetUp"];

    [Test]
    public void New_fixtures_arrange_their_subject_in_the_test_rather_than_a_setup_method()
    {
        var root = ArchitectureFixture.FindRepositoryRoot(TestContext.CurrentContext.TestDirectory);
        var violations = ArchitectureFixture.TestSourceFiles(root)
            .Where(HasSetUpMethod)
            .Select(path => ArchitectureFixture.Relative(root, path).Replace('\\', '/'))
            .ToArray();

        Assert.That(violations, Is.Empty,
            "A new fixture must arrange its subject inside the test, through a local helper taking explicit parameters.");
    }

    private static bool HasSetUpMethod(string path)
    {
        var (_, node) = ArchitectureFixture.ParseSourceFile(path);
        if (HasAttribute(node, "SetUpFixture"))
            return false;

        return node.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .SelectMany(method => method.AttributeLists.SelectMany(list => list.Attributes))
            .Any(attribute => SetUpAttributes.Contains(
                ArchitectureFixture.FinalTypeSegment(attribute.Name).Replace("Attribute", string.Empty),
                StringComparer.Ordinal));
    }

    private static bool HasAttribute(Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax node, string name)
        => node.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .SelectMany(type => type.AttributeLists.SelectMany(list => list.Attributes))
            .Any(attribute => string.Equals(
                ArchitectureFixture.FinalTypeSegment(attribute.Name).Replace("Attribute", string.Empty),
                name,
                StringComparison.Ordinal));
}

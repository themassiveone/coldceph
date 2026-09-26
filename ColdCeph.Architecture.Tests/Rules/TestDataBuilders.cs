using ColdCeph.Architecture.Tests.Fixtures;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ColdCeph.Architecture.Tests.Rules;

[TestFixture]
public sealed class TestDataBuilders
{
    private const int MaximumInlineConstructions = 15;
    private const int MaximumPositionalParameters = 3;
    private const string SupportFolder = "Support";

    [Test]
    public void Types_built_many_times_in_one_test_project_have_a_builder()
    {
        var root = ArchitectureFixture.FindRepositoryRoot(TestContext.CurrentContext.TestDirectory);
        var violations = ArchitectureFixture.TestProjects
            .Where(project => project.EndsWith(".Tests", StringComparison.Ordinal) && project != "ColdCeph.Architecture.Tests")
            .SelectMany(project => HotspotTypes(root, project)
                .Where(hotspot => !BuilderExists(root, project, hotspot.Type))
                .Select(hotspot =>
                    $"{project} builds {hotspot.Type} inline {hotspot.Count} times: add {project}/{SupportFolder}/{hotspot.Type}Builder.cs"))
            .ToArray();

        Assert.That(violations, Is.Empty,
            $"A production DTO or model constructed more than {MaximumInlineConstructions} times in one test project must use a builder.");
    }

    [Test]
    public void Tests_do_not_construct_a_type_that_already_has_a_builder()
    {
        var root = ArchitectureFixture.FindRepositoryRoot(TestContext.CurrentContext.TestDirectory);
        var violations = ArchitectureFixture.TestProjects
            .Where(project => Directory.Exists(Path.Join(root, project)))
            .SelectMany(project => BuiltTypes(root, project)
                .SelectMany(type => ArchitectureFixture.ProjectSourceFiles(root, project)
                    .Where(path => !ArchitectureFixture.HasPathPart(root, path, SupportFolder))
                    .SelectMany(path => ConstructionSites(root, path, type))))
            .ToArray();

        Assert.That(violations, Is.Empty,
            "A type with a Support/ builder must be built through it.");
    }

    private static HashSet<string> CandidateTypes(string root, string testProject)
    {
        var sibling = testProject.EndsWith(".Tests", StringComparison.Ordinal)
            ? testProject[..^".Tests".Length]
            : null;
        if (sibling is null || !Directory.Exists(Path.Join(root, sibling)))
            return [];

        return ArchitectureFixture.ProjectSourceFiles(root, sibling)
            .Where(path => IsDataFolder(root, path))
            .SelectMany(WideTypeNames)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool IsDataFolder(string root, string path)
    {
        var parts = ArchitectureFixture.Parts(root, path);
        var folder = parts.Length >= 2 ? parts[^2] : string.Empty;
        return folder is "DTOs" or "Models" && parts.Contains("Features", StringComparer.Ordinal);
    }

    private static IEnumerable<string> WideTypeNames(string path)
    {
        var (_, node) = ArchitectureFixture.ParseSourceFile(path);
        return node.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Where(declaration => declaration.ParameterList?.Parameters.Count > MaximumPositionalParameters
                                  || declaration.Members.OfType<ConstructorDeclarationSyntax>()
                                      .Any(constructor => constructor.ParameterList.Parameters.Count > MaximumPositionalParameters))
            .Select(declaration => declaration.Identifier.Text);
    }

    private static IEnumerable<(string Type, int Count)> HotspotTypes(string root, string project)
    {
        if (!Directory.Exists(Path.Join(root, project)))
            return [];

        var candidates = CandidateTypes(root, project);
        if (candidates.Count == 0)
            return [];

        return ArchitectureFixture.ProjectSourceFiles(root, project)
            .SelectMany(ConstructedTypeNames)
            .Where(candidates.Contains)
            .GroupBy(type => type, StringComparer.Ordinal)
            .Where(group => group.Count() > MaximumInlineConstructions)
            .Select(group => (group.Key, group.Count()));
    }

    private static HashSet<string> BuiltTypes(string root, string project)
    {
        var support = Path.Join(root, project, SupportFolder);
        if (!Directory.Exists(support))
            return [];

        var candidates = CandidateTypes(root, project);
        return Directory.EnumerateFiles(support, "*Builder.cs")
            .Select(path => Path.GetFileNameWithoutExtension(path)[..^"Builder".Length])
            .Where(candidates.Contains)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool BuilderExists(string root, string project, string type)
        => File.Exists(Path.Join(root, project, SupportFolder, $"{type}Builder.cs"));

    private static IEnumerable<string> ConstructedTypeNames(string path)
    {
        var (_, node) = ArchitectureFixture.ParseSourceFile(path);
        return node.DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>()
            .Select(creation => ArchitectureFixture.FinalTypeSegment(creation.Type));
    }

    private static IEnumerable<string> ConstructionSites(string root, string path, string type)
    {
        var (tree, node) = ArchitectureFixture.ParseSourceFile(path);
        return node.DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>()
            .Where(creation => ArchitectureFixture.FinalTypeSegment(creation.Type) == type)
            .Select(creation => $"{ArchitectureFixture.Location(root, path, tree, creation)}: {type} is built inline; use {type}Builder");
    }
}

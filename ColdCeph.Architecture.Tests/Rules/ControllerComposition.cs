using ColdCeph.Architecture.Tests.Fixtures;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ColdCeph.Architecture.Tests.Rules;

[TestFixture]
public sealed class ControllerComposition
{
    [Test]
    public void Controllers_do_not_construct_services_providers_or_repositories()
    {
        var root = ArchitectureFixture.FindRepositoryRoot(TestContext.CurrentContext.TestDirectory);
        var typeSuffixes = new[] { "Service", "Provider", "Repository" };
        var violations = ArchitectureFixture.ProductionSourceFiles(root)
            .Where(path => ArchitectureFixture.HasPathPart(root, path, "Controllers"))
            .SelectMany(path => FindConstructions(root, path, typeSuffixes))
            .ToArray();

        Assert.That(violations, Is.Empty,
            "Controllers must receive dependencies through constructors.");
    }

    [Test]
    public void Controllers_are_not_static()
    {
        var root = ArchitectureFixture.FindRepositoryRoot(TestContext.CurrentContext.TestDirectory);
        var violations = ArchitectureFixture.ProductionSourceFiles(root)
            .Where(path => ArchitectureFixture.HasPathPart(root, path, "Controllers"))
            .SelectMany(path =>
            {
                var (tree, rootNode) = ArchitectureFixture.ParseSourceFile(path);
                return rootNode.DescendantNodes()
                    .OfType<TypeDeclarationSyntax>()
                    .Where(type => type.Identifier.Text.EndsWith("Controller", StringComparison.Ordinal)
                                   && type.Modifiers.Any(modifier => modifier.RawKind == (int)SyntaxKind.StaticKeyword))
                    .Select(type => $"{ArchitectureFixture.Location(root, path, tree, type)}: static {type.Identifier.Text}");
            })
            .ToArray();

        Assert.That(violations, Is.Empty,
            "Controllers must be constructor-injected boundaries.");
    }

    [Test]
    public void Controllers_do_not_depend_on_providers_or_repositories()
    {
        var root = ArchitectureFixture.FindRepositoryRoot(TestContext.CurrentContext.TestDirectory);
        var violations = ArchitectureFixture.ProductionSourceFiles(root)
            .Where(path => ArchitectureFixture.HasPathPart(root, path, "Controllers"))
            .SelectMany(path => FindLowLevelDependencies(root, path))
            .ToArray();

        Assert.That(violations, Is.Empty,
            "Controllers must route to domain services; providers and repositories stay behind services.");
    }

    [Test]
    public void Controllers_stay_thin()
    {
        var root = ArchitectureFixture.FindRepositoryRoot(TestContext.CurrentContext.TestDirectory);
        var violations = ArchitectureFixture.ProductionSourceFiles(root)
            .Where(path => ArchitectureFixture.HasPathPart(root, path, "Controllers"))
            .SelectMany(path => FindComplexControllerMethods(root, path))
            .ToArray();

        Assert.That(violations, Is.Empty,
            "Controller methods must stay at routing complexity: no loops/switch/try/catch, cyclomatic complexity <= 2, and <= 5 statements.");
    }

    [Test]
    public void Feature_slices_do_not_import_sibling_slice_internals()
    {
        var root = ArchitectureFixture.FindRepositoryRoot(TestContext.CurrentContext.TestDirectory);
        var violations = ArchitectureFixture.ProductionSourceFiles(root)
            .Select(path => (Path: path, Parts: ArchitectureFixture.Parts(root, path)))
            .Where(item => ArchitectureFixture.TryFeatureLocation(item.Parts, out _, out _, out _)
                           || IsEntrypoint(item.Parts))
            .SelectMany(item => FindCrossSliceInternalUsings(root, item.Path, item.Parts))
            .ToArray();

        Assert.That(violations, Is.Empty,
            "Cross-slice traffic goes through Controllers/ and DTOs/ only.");
    }

    private static IEnumerable<string> FindConstructions(string root, string path, string[] typeSuffixes)
    {
        var (tree, rootNode) = ArchitectureFixture.ParseSourceFile(path);
        return rootNode.DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>()
            .Where(node => typeSuffixes.Any(suffix => ArchitectureFixture.FinalTypeSegment(node.Type).EndsWith(suffix, StringComparison.Ordinal)))
            .Select(creation => $"{ArchitectureFixture.Location(root, path, tree, creation)}: new {creation.Type}(...)");
    }

    private static IEnumerable<string> FindLowLevelDependencies(string root, string path)
    {
        var (tree, rootNode) = ArchitectureFixture.ParseSourceFile(path);
        foreach (var usingDirective in rootNode.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            var name = usingDirective.Name?.ToString();
            if (name is null)
                continue;
            if (name.Contains(".Providers", StringComparison.Ordinal) || name.Contains(".Repositories", StringComparison.Ordinal))
                yield return $"{ArchitectureFixture.Location(root, path, tree, usingDirective)}: using {name}";
        }

        var forbiddenSuffixes = new[] { "Provider", "Repository" };
        foreach (var parameter in rootNode.DescendantNodes().OfType<ParameterSyntax>()
                     .Where(parameter => parameter.Type is not null && forbiddenSuffixes.Any(suffix => ArchitectureFixture.FinalTypeSegment(parameter.Type).EndsWith(suffix, StringComparison.Ordinal))))
            yield return $"{ArchitectureFixture.Location(root, path, tree, parameter)}: parameter {parameter.Type}";
    }

    private static IEnumerable<string> FindComplexControllerMethods(string root, string path)
    {
        var (tree, rootNode) = ArchitectureFixture.ParseSourceFile(path);
        return rootNode.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(method => !method.Modifiers.Any(modifier => modifier.RawKind == (int)SyntaxKind.StaticKeyword))
            .SelectMany(method =>
            {
                var violations = new List<string>();
                var location = ArchitectureFixture.Location(root, path, tree, method);
                var statementCount = method.Body?.Statements.Count ?? (method.ExpressionBody is null ? 0 : 1);
                var complexity = CyclomaticComplexity(method);

                if (statementCount > 5)
                    violations.Add($"{location}: {method.Identifier.Text} has {statementCount} statements");
                if (complexity > 2)
                    violations.Add($"{location}: {method.Identifier.Text} has cyclomatic complexity {complexity}");
                if (method.DescendantNodes().Any(node => node is ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax))
                    violations.Add($"{location}: {method.Identifier.Text} contains a loop");
                if (method.DescendantNodes().Any(node => node is SwitchStatementSyntax or SwitchExpressionSyntax))
                    violations.Add($"{location}: {method.Identifier.Text} contains a switch");
                if (method.DescendantNodes().Any(node => node is TryStatementSyntax or CatchClauseSyntax))
                    violations.Add($"{location}: {method.Identifier.Text} contains try/catch");

                return violations;
            });
    }

    private static int CyclomaticComplexity(MethodDeclarationSyntax method)
    {
        var complexity = 1;
        foreach (var node in method.DescendantNodes())
        {
            complexity += node switch
            {
                IfStatementSyntax => 1,
                ConditionalExpressionSyntax => 1,
                ForStatementSyntax => 1,
                ForEachStatementSyntax => 1,
                WhileStatementSyntax => 1,
                DoStatementSyntax => 1,
                CaseSwitchLabelSyntax => 1,
                SwitchExpressionArmSyntax => 1,
                BinaryExpressionSyntax binary when binary.RawKind is (int)SyntaxKind.LogicalAndExpression or (int)SyntaxKind.LogicalOrExpression => 1,
                _ => 0
            };
        }

        return complexity;
    }

    private static IEnumerable<string> FindCrossSliceInternalUsings(string root, string path, string[] sourceParts)
    {
        var sourceIsFeature = ArchitectureFixture.TryFeatureLocation(sourceParts, out var sourceProject, out var sourceSlice, out _);
        if (!sourceIsFeature && !IsEntrypoint(sourceParts))
            yield break;

        var (tree, rootNode) = ArchitectureFixture.ParseSourceFile(path);
        var project = sourceIsFeature ? sourceProject : sourceParts[0];
        var prefix = $"{project}.Features.";
        foreach (var usingDirective in rootNode.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            var name = usingDirective.Name?.ToString();
            if (name is null || !name.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            var targetParts = name[prefix.Length..].Split('.');
            if (targetParts.Length < 2)
                continue;
            if (sourceIsFeature && targetParts[0] == sourceSlice)
                continue;
            if (targetParts[1] is "Controllers" or "DTOs" or "ViewModels")
                continue;

            yield return $"{ArchitectureFixture.Location(root, path, tree, usingDirective)}: using {name}";
        }
    }

    private static bool IsEntrypoint(string[] parts)
        => parts.Length == 2 && parts[1] == "Program.cs";
}

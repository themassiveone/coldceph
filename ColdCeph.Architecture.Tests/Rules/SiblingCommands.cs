using ColdCeph.Architecture.Tests.Fixtures;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ColdCeph.Architecture.Tests.Rules;

[TestFixture]
public sealed class SiblingCommands
{
    [Test]
    public void Production_call_sites_outside_a_slice_do_not_invoke_that_slice_command_methods()
    {
        var root = ArchitectureFixture.FindRepositoryRoot(TestContext.CurrentContext.TestDirectory);
        var violations = ArchitectureFixture.ProductionSourceFiles(root)
            .SelectMany(path => FindForeignCommandCalls(root, path))
            .ToArray();

        Assert.That(violations, Is.Empty,
            "Siblings may only call Get*/List*/Has*/Is* on another slice controller.");
    }

    private static IEnumerable<string> FindForeignCommandCalls(string root, string path)
    {
        var parts = ArchitectureFixture.Parts(root, path);
        ArchitectureFixture.TryFeatureLocation(parts, out _, out var sourceSlice, out _);
        var (tree, rootNode) = ArchitectureFixture.ParseSourceFile(path);

        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameter in rootNode.DescendantNodes().OfType<ParameterSyntax>())
        {
            if (parameter.Type is null)
                continue;
            var typeName = ArchitectureFixture.FinalTypeSegment(parameter.Type);
            if (typeName.EndsWith("Controller", StringComparison.Ordinal))
                bindings[parameter.Identifier.Text] = typeName;
        }

        foreach (var field in rootNode.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (field.Parent is not VariableDeclarationSyntax declaration)
                continue;
            var typeName = ArchitectureFixture.FinalTypeSegment(declaration.Type);
            if (typeName.EndsWith("Controller", StringComparison.Ordinal))
                bindings[field.Identifier.Text] = typeName;
        }

        foreach (var invocation in rootNode.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax member)
                continue;
            if (member.Expression is not IdentifierNameSyntax identifier)
                continue;
            if (!bindings.TryGetValue(identifier.Identifier.Text, out var controllerType))
                continue;

            var method = member.Name.Identifier.Text;
            if (ArchitectureFixture.IsQueryMethod(method))
                continue;

            var slice = SliceFromControllerName(controllerType);
            if (slice is null || slice == sourceSlice)
                continue;

            yield return $"{ArchitectureFixture.Location(root, path, tree, invocation)}: {controllerType}.{method} is a command on {slice}";
        }
    }

    private static string? SliceFromControllerName(string controllerName)
    {
        const string suffix = "Controller";
        if (!controllerName.EndsWith(suffix, StringComparison.Ordinal))
            return null;

        var stem = controllerName[..^suffix.Length];
        foreach (var known in ArchitectureFixture.AllowedControlSlices.Concat(ArchitectureFixture.AllowedAgentSlices).Distinct())
        {
            if (stem.StartsWith(known, StringComparison.Ordinal))
                return known;
        }

        return null;
    }
}

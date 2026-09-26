using ArchUnitNET.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ColdCeph.Architecture.Tests.Fixtures;

internal static class ArchitectureFixture
{
    public static readonly string[] ProductionProjects =
    [
        "ColdCeph.Core",
        "ColdCeph.Node",
        "ColdCeph.Control"
    ];

    public static readonly string[] TestProjects =
    [
        "ColdCeph.Core.Tests",
        "ColdCeph.Node.Tests",
        "ColdCeph.Control.Tests",
        "ColdCeph.Architecture.Tests",
        "ColdCeph.E2E.Tests"
    ];

    public static readonly string[] AllowedControlSlices =
    [
        "S3",
        "StoragePlane",
        "Integrity",
        "Osds",
        "Devices",
        "Hosts",
        "Operations",
        "Auth"
    ];

    public static readonly string[] AllowedNodeSlices =
    [
        "Osds",
        "Devices",
        "Hosts"
    ];

    public static readonly string[] AllowedCoreSlices =
    [
        "S3",
        "StoragePlane",
        "Integrity",
        "Osds",
        "Devices",
        "Hosts",
        "Operations",
        "Auth"
    ];

    public static readonly string[] ForbiddenSliceNames =
    [
        "Dashboard",
        "Ceph",
        "ClusterState",
        "Nodes"
    ];

    public static readonly string[] AllowedFeatureTypeFolders =
    [
        "Controllers",
        "DTOs",
        "Interfaces",
        "Models",
        "Providers",
        "Repositories",
        "Services",
        "ViewModels",
        "Views"
    ];

    public static readonly string[] AllowedTestKindFolders =
    [
        "Controller",
        "HTTP",
        "Provider",
        "Repository",
        "Unit"
    ];

    public static readonly string[] AllowedRootTestSupportFolders =
    [
        "Fake",
        "Fixtures",
        "Support",
        "E2E",
        "Adapters",
        "Scenarios",
        "Backend",
        "Extensions",
        "States"
    ];

    public static readonly string[] QueryMethodPrefixes =
    [
        "Get",
        "List",
        "Has",
        "Is",
        "Peek",
        "Query"
    ];

    public static readonly ArchUnitNET.Domain.Architecture ArchUnitArchitecture = new ArchLoader()
        .LoadAssemblies(ProductionProjects.Select(System.Reflection.Assembly.Load).ToArray())
        .Build();

    public static string FindRepositoryRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Join(directory.FullName, "coldceph.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }

    public static IEnumerable<string> ProductionSourceFiles(string repositoryRoot)
        => ProductionProjects.SelectMany(project => ProjectSourceFiles(repositoryRoot, project));

    public static IEnumerable<string> TestSourceFiles(string repositoryRoot)
        => TestProjects
            .Where(project => Directory.Exists(Path.Join(repositoryRoot, project)))
            .SelectMany(project => ProjectSourceFiles(repositoryRoot, project));

    public static IEnumerable<string> ProjectSourceFiles(string repositoryRoot, string project)
        => Directory.EnumerateFiles(Path.Join(repositoryRoot, project), "*.cs", SearchOption.AllDirectories)
            .Where(path => !HasPathPart(repositoryRoot, path, "bin") && !HasPathPart(repositoryRoot, path, "obj"));

    public static (SyntaxTree Tree, CompilationUnitSyntax Root) ParseSourceFile(string path)
    {
        var source = File.ReadAllText(path);
        var tree = CSharpSyntaxTree.ParseText(source);
        return (tree, tree.GetCompilationUnitRoot());
    }

    public static string Location(string root, string path, SyntaxTree tree, SyntaxNode node)
    {
        var lineSpan = tree.GetLineSpan(node.Span);
        return $"{Relative(root, path)}:{lineSpan.StartLinePosition.Line + 1}";
    }

    public static string FinalTypeSegment(TypeSyntax type)
    {
        var value = type switch
        {
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            NullableTypeSyntax nullable => FinalTypeSegment(nullable.ElementType),
            ArrayTypeSyntax array => FinalTypeSegment(array.ElementType),
            _ => type.ToString()
        };

        var genericDelimiter = value.IndexOf('<', StringComparison.Ordinal);
        if (genericDelimiter >= 0)
            value = value[..genericDelimiter];

        return value.Split('.').Last();
    }

    public static bool HasPathPart(string root, string path, string part)
        => Parts(root, path).Contains(part, StringComparer.Ordinal);

    public static string[] Parts(string root, string path)
        => Relative(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static string Relative(string root, string path)
        => Path.GetRelativePath(root, path);

    public static bool TryFeatureLocation(string[] parts, out string project, out string slice, out string typeFolder)
    {
        project = string.Empty;
        slice = string.Empty;
        typeFolder = string.Empty;

        if (parts.Length < 4 || parts[1] != "Features")
            return false;

        project = parts[0];
        slice = parts[2];
        typeFolder = parts.Length >= 5 ? parts[3] : "(none)";
        return true;
    }

    public static bool IsQueryMethod(string name)
        => QueryMethodPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));
}

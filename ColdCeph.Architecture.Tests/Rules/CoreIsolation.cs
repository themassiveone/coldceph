using ColdCeph.Architecture.Tests.Fixtures;

namespace ColdCeph.Architecture.Tests.Rules;

[TestFixture]
public sealed class CoreIsolation
{
    private static readonly string[] ForbiddenTokens =
    [
        "WebApplication",
        "HttpClient",
        "MapGet",
        "UseKestrel",
        "systemctl",
        "hdparm",
        "ceph --format",
        "Microsoft.AspNetCore"
    ];

    [Test]
    public void Core_has_no_http_ceph_cli_or_systemd()
    {
        var root = ArchitectureFixture.FindRepositoryRoot(TestContext.CurrentContext.TestDirectory);
        var violations = ArchitectureFixture.ProjectSourceFiles(root, "ColdCeph.Core")
            .SelectMany(path =>
            {
                var text = File.ReadAllText(path);
                return ForbiddenTokens
                    .Where(token => text.Contains(token, StringComparison.Ordinal))
                    .Select(token => $"{ArchitectureFixture.Relative(root, path)} contains '{token}'");
            })
            .ToArray();

        Assert.That(violations, Is.Empty,
            "ColdCeph.Core is DTOs and IDs only: no HTTP, no ceph CLI, no systemd.");
    }

    [Test]
    public void Core_feature_files_are_dtos_only()
    {
        var root = ArchitectureFixture.FindRepositoryRoot(TestContext.CurrentContext.TestDirectory);
        var violations = ArchitectureFixture.ProjectSourceFiles(root, "ColdCeph.Core")
            .Select(path => (Path: path, Parts: ArchitectureFixture.Parts(root, path)))
            .Where(item => ArchitectureFixture.TryFeatureLocation(item.Parts, out _, out _, out var folder)
                           && folder != "DTOs")
            .Select(item => ArchitectureFixture.Relative(root, item.Path))
            .ToArray();

        Assert.That(violations, Is.Empty,
            "ColdCeph.Core slices may only contain DTOs.");
    }
}

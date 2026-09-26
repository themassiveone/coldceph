using ColdCeph.Debug.Features.Pages.DTOs;

namespace ColdCeph.Debug.Features.Pages.Services;

public sealed class OperatorPageCatalog
{
    public IReadOnlyList<OperatorPageDto> All { get; } =
    [
        new("/auth/login", "Login", true),
        new("/", "Storage", false),
        new("/integrity", "Integrity", false),
        new("/hosts", "Hosts", false),
        new("/osds", "OSDs", false),
        new("/devices", "Devices", false),
        new("/s3", "S3", false),
        new("/operations", "Operations", false)
    ];

    public OperatorPageDto Resolve(string path)
    {
        var normalized = Normalize(path);
        if (normalized.StartsWith("/v1", StringComparison.Ordinal))
            throw new ArgumentException("cc-debug captures operator HTML, not /v1.");

        var page = All.FirstOrDefault(item => item.Path == normalized);
        if (page is null)
            throw new ArgumentException($"Unknown operator page '{path}'.");
        return page;
    }

    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Page path is required.");
        var trimmed = path.Trim();
        if (!trimmed.StartsWith('/'))
            trimmed = "/" + trimmed;
        if (trimmed.Length > 1)
            trimmed = trimmed.TrimEnd('/');
        return trimmed;
    }
}

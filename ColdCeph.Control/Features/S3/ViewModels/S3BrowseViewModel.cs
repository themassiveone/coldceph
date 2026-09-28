using ColdCeph.Core.Features.S3.DTOs;
using ColdCeph.Core.Features.StoragePlane.DTOs;

namespace ColdCeph.Control.Features.S3.ViewModels;

public sealed record S3BrowseViewModel
{
    public required StoragePlaneState State { get; init; }
    public required bool BrowseReady { get; init; }
    public required bool CanWake { get; init; }
    public required string Banner { get; init; }
    public string? Error { get; init; }
    public string? Bucket { get; init; }
    public string Prefix { get; init; } = "";
    public IReadOnlyList<S3BucketDto> Buckets { get; init; } = [];
    public IReadOnlyList<S3PrefixDto> Prefixes { get; init; } = [];
    public IReadOnlyList<S3ObjectDto> Objects { get; init; } = [];
    public IReadOnlyList<(string Label, string Href)> Crumbs { get; init; } = [];

    public string WakeLabel => State == StoragePlaneState.Faulted ? "Resume storage" : "Wake storage";

    public string FolderHref(S3PrefixDto folder)
        => $"/s3/{Uri.EscapeDataString(Bucket ?? "")}/{EscapePath(folder.Prefix)}";

    public string DownloadHref(S3ObjectDto item)
        => $"/s3/{Uri.EscapeDataString(Bucket ?? "")}/download/{EscapePath(item.Key)}";

    public static string FormatSize(long size)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)size;
        var unit = 0;
        while (value >= 1000 && unit < units.Length - 1)
        {
            value /= 1000;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }

    private static string EscapePath(string path)
        => string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
}

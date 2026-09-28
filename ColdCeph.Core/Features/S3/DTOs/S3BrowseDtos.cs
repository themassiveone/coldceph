namespace ColdCeph.Core.Features.S3.DTOs;

public sealed record S3BucketDto
{
    public required string Name { get; init; }
}

public sealed record S3ObjectListingDto
{
    public required string Bucket { get; init; }
    public required string Prefix { get; init; }
    public required IReadOnlyList<S3PrefixDto> Prefixes { get; init; }
    public required IReadOnlyList<S3ObjectDto> Objects { get; init; }
}

public sealed record S3PrefixDto
{
    public required string Name { get; init; }
    public required string Prefix { get; init; }
}

public sealed record S3ObjectDto
{
    public required string Name { get; init; }
    public required string Key { get; init; }
    public required long Size { get; init; }
}

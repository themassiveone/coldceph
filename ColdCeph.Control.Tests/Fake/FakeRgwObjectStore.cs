using ColdCeph.Control.Features.S3.Interfaces;
using ColdCeph.Core.Features.S3.DTOs;

namespace ColdCeph.Control.Tests.Fake;

public sealed class FakeRgwObjectStore : IRgwObjectStore
{
    private readonly Dictionary<string, Dictionary<string, byte[]>> _buckets = new(StringComparer.Ordinal);

    public void SeedBucket(string name)
        => _buckets[name] = new Dictionary<string, byte[]>(StringComparer.Ordinal);

    public void SeedObject(string bucket, string key, byte[] body)
    {
        if (!_buckets.TryGetValue(bucket, out var objects))
        {
            objects = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            _buckets[bucket] = objects;
        }

        objects[key] = body;
    }

    public IReadOnlyList<S3BucketDto> ListBuckets()
        => _buckets.Keys.OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new S3BucketDto { Name = name })
            .ToArray();

    public S3ObjectListingDto ListObjects(string bucket, string prefix)
    {
        if (!_buckets.TryGetValue(bucket, out var objects))
            throw new InvalidOperationException($"RGW GET /{bucket} failed: 404");
        prefix ??= "";
        var prefixes = new SortedSet<string>(StringComparer.Ordinal);
        var items = new List<S3ObjectDto>();
        foreach (var (key, body) in objects)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal) || key == prefix)
                continue;
            var rest = key[prefix.Length..];
            var slash = rest.IndexOf('/');
            if (slash >= 0)
            {
                prefixes.Add(prefix + rest[..(slash + 1)]);
                continue;
            }

            if (key.EndsWith('/'))
                continue;
            items.Add(new S3ObjectDto
            {
                Key = key,
                Name = S3KeyRules.FileName(key),
                Size = body.Length
            });
        }

        return new S3ObjectListingDto
        {
            Bucket = bucket,
            Prefix = prefix,
            Prefixes = prefixes.Select(value => new S3PrefixDto { Prefix = value, Name = S3KeyRules.FileName(value) }).ToArray(),
            Objects = items
        };
    }

    public Stream OpenObject(string bucket, string key)
    {
        if (!_buckets.TryGetValue(bucket, out var objects) || !objects.TryGetValue(key, out var body))
            throw new InvalidOperationException($"RGW GET /{bucket}/{key} failed: 404");
        return new MemoryStream(body, writable: false);
    }

    public void PutObject(string bucket, string key, Stream body, string contentType)
    {
        _ = contentType;
        if (!_buckets.ContainsKey(bucket))
            throw new InvalidOperationException($"RGW PUT /{bucket}/{key} failed: 404");
        using var copy = new MemoryStream();
        body.CopyTo(copy);
        SeedObject(bucket, key, copy.ToArray());
    }
}

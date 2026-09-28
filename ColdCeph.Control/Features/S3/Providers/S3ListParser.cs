using System.Xml.Linq;
using ColdCeph.Core.Features.S3.DTOs;

namespace ColdCeph.Control.Features.S3.Providers;

public static class S3ListParser
{
    public static IReadOnlyList<S3BucketDto> ParseBuckets(string xml)
    {
        var document = XDocument.Parse(string.IsNullOrWhiteSpace(xml) ? "<ListAllMyBucketsResult/>" : xml);
        return document.Descendants().Where(element => element.Name.LocalName == "Bucket")
            .Select(bucket => bucket.Elements().FirstOrDefault(child => child.Name.LocalName == "Name")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => new S3BucketDto { Name = name! })
            .ToArray();
    }

    public static S3ObjectListingDto ParseObjects(string bucket, string prefix, string xml)
    {
        var document = XDocument.Parse(string.IsNullOrWhiteSpace(xml) ? "<ListBucketResult/>" : xml);
        var prefixes = document.Descendants()
            .Where(element => element.Name.LocalName == "CommonPrefixes")
            .Select(item => item.Elements().FirstOrDefault(child => child.Name.LocalName == "Prefix")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => new S3PrefixDto
            {
                Prefix = value!,
                Name = S3KeyRules.FileName(value!)
            })
            .ToArray();
        var objects = document.Descendants()
            .Where(element => element.Name.LocalName == "Contents")
            .Select(ParseObject)
            .Where(item => item is not null && item.Key != prefix && !item.Key.EndsWith('/'))
            .Select(item => item!)
            .ToArray();
        return new S3ObjectListingDto
        {
            Bucket = bucket,
            Prefix = prefix,
            Prefixes = prefixes,
            Objects = objects
        };
    }

    private static S3ObjectDto? ParseObject(XElement contents)
    {
        var key = contents.Elements().FirstOrDefault(child => child.Name.LocalName == "Key")?.Value;
        if (string.IsNullOrWhiteSpace(key))
            return null;
        var sizeText = contents.Elements().FirstOrDefault(child => child.Name.LocalName == "Size")?.Value;
        _ = long.TryParse(sizeText, out var size);
        return new S3ObjectDto
        {
            Key = key,
            Name = S3KeyRules.FileName(key),
            Size = size
        };
    }
}

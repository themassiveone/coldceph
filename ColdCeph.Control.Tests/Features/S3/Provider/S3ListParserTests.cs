using ColdCeph.Control.Features.S3.Providers;

namespace ColdCeph.Control.Tests.Features.S3.Provider;

[TestFixture]
public sealed class S3ListParserTests
{
    [Test]
    public void ParseBuckets_reads_bucket_names()
    {
        const string xml = """
            <ListAllMyBucketsResult xmlns="http://s3.amazonaws.com/doc/2006-03-01/">
              <Buckets>
                <Bucket><Name>cold</Name></Bucket>
                <Bucket><Name>warm</Name></Bucket>
              </Buckets>
            </ListAllMyBucketsResult>
            """;

        var buckets = S3ListParser.ParseBuckets(xml);

        Assert.That(buckets.Select(bucket => bucket.Name), Is.EqualTo(new[] { "cold", "warm" }));
    }

    [Test]
    public void ParseBuckets_empty_xml_is_no_buckets()
    {
        var buckets = S3ListParser.ParseBuckets("");

        Assert.That(buckets, Is.Empty);
    }

    [Test]
    public void ParseObjects_splits_folders_from_objects()
    {
        const string xml = """
            <ListBucketResult xmlns="http://s3.amazonaws.com/doc/2006-03-01/">
              <Prefix></Prefix>
              <CommonPrefixes><Prefix>photos/</Prefix></CommonPrefixes>
              <Contents><Key>readme.txt</Key><Size>5</Size></Contents>
              <Contents><Key>photos/</Key><Size>0</Size></Contents>
            </ListBucketResult>
            """;

        var listing = S3ListParser.ParseObjects("cold", "", xml);

        Assert.That(listing.Prefixes.Select(prefix => prefix.Name), Is.EqualTo(new[] { "photos" }));
        Assert.That(listing.Objects.Select(item => item.Name), Is.EqualTo(new[] { "readme.txt" }));
        Assert.That(listing.Objects[0].Size, Is.EqualTo(5));
    }

    [Test]
    public void ParseObjects_skips_the_current_prefix_marker()
    {
        const string xml = """
            <ListBucketResult>
              <Prefix>photos/</Prefix>
              <Contents><Key>photos/</Key><Size>0</Size></Contents>
              <Contents><Key>photos/cat.jpg</Key><Size>3</Size></Contents>
            </ListBucketResult>
            """;

        var listing = S3ListParser.ParseObjects("cold", "photos/", xml);

        Assert.That(listing.Objects.Select(item => item.Key), Is.EqualTo(new[] { "photos/cat.jpg" }));
        Assert.That(listing.Prefixes, Is.Empty);
    }
}

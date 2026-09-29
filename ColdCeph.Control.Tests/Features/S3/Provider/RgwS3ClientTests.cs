using System.Net;
using System.Text;
using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.S3.Providers;
using ColdCeph.Control.Tests.Fake;
using ColdCeph.Control.Tests.Support;

namespace ColdCeph.Control.Tests.Features.S3.Provider;

/// <summary>
/// A hand-rolled SigV4 signer with no tests at all. A signature that is wrong fails with a 403
/// from RGW and nothing else, so what matters is that every element the signature covers really
/// reaches the wire and really changes the signature.
/// </summary>
[TestFixture]
public sealed class RgwS3ClientTests
{
    private const string BucketsXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <ListAllMyBucketsResult xmlns="http://s3.amazonaws.com/doc/2006-03-01/">
          <Owner><ID>coldceph</ID></Owner>
          <Buckets><Bucket><Name>cold</Name><CreationDate>2026-09-01T00:00:00.000Z</CreationDate></Bucket></Buckets>
        </ListAllMyBucketsResult>
        """;

    // ---- request shape ------------------------------------------------------

    [Test]
    public void Listing_buckets_gets_the_service_root()
    {
        var (client, handler) = Create(BucketsXml);

        var buckets = client.ListBuckets();

        Assert.That(handler.LastRequest!.Method.Method, Is.EqualTo("GET"));
        Assert.That(handler.LastRequest.RequestUri!.AbsolutePath, Is.EqualTo("/"));
        Assert.That(buckets.Select(bucket => bucket.Name), Is.EquivalentTo(new[] { "cold" }));
    }

    [Test]
    public void Listing_objects_uses_a_delimited_v2_listing()
    {
        var (client, handler) = Create("<ListBucketResult></ListBucketResult>");

        _ = client.ListObjects("cold", "photos/2026/");

        var query = handler.LastRequest!.RequestUri!.Query;
        Assert.That(handler.LastRequest.RequestUri.AbsolutePath, Is.EqualTo("/cold"));
        Assert.That(query, Does.Contain("list-type=2"));
        Assert.That(query, Does.Contain("delimiter=%2F"));
        Assert.That(query, Does.Contain("prefix=photos%2F2026%2F"));
    }

    [Test]
    public void Opening_an_object_gets_its_key_path()
    {
        var (client, handler) = Create("the stored bytes");

        using var stream = client.OpenObject("cold", "photos/summer.jpg");

        Assert.That(handler.LastRequest!.RequestUri!.AbsolutePath, Is.EqualTo("/cold/photos/summer.jpg"));
        using var reader = new StreamReader(stream);
        Assert.That(reader.ReadToEnd(), Is.EqualTo("the stored bytes"));
    }

    [Test]
    public void Putting_an_object_sends_the_body_and_content_type()
    {
        var (client, handler) = Create("");

        client.PutObject("cold", "notes.txt", new MemoryStream(Encoding.UTF8.GetBytes("hello")), "text/plain");

        Assert.That(handler.LastRequest!.Method.Method, Is.EqualTo("PUT"));
        Assert.That(handler.LastBody, Is.EqualTo("hello"));
        Assert.That(handler.LastRequest.Content!.Headers.ContentType!.MediaType, Is.EqualTo("text/plain"));
    }

    /// <summary>
    /// The canonical path is signed, so the path on the wire has to be byte-identical to it.
    /// Anything that re-canonicalises percent-escapes between signing and sending leaves the two
    /// disagreeing, and RGW answers 403 with nothing else to go on.
    /// </summary>
    [TestCase("holiday photo.jpg", "/cold/holiday%20photo.jpg")]
    [TestCase("a+b.txt", "/cold/a%2Bb.txt")]
    [TestCase("naïve.txt", "/cold/na%C3%AFve.txt")]
    [TestCase("100% done.txt", "/cold/100%25%20done.txt")]
    [TestCase("photos/summer.jpg", "/cold/photos/summer.jpg")]
    public void An_awkward_key_reaches_the_wire_exactly_as_it_was_signed(string key, string expected)
    {
        var (client, handler) = Create("bytes");

        using var _ = client.OpenObject("cold", key);

        Assert.That(handler.LastRequest!.RequestUri!.PathAndQuery, Is.EqualTo(expected));
        Assert.That(Uri.UnescapeDataString(handler.LastRequest.RequestUri.PathAndQuery), Is.EqualTo($"/cold/{key}"));
    }

    [Test]
    public void A_listing_query_reaches_the_wire_exactly_as_it_was_signed()
    {
        var (client, handler) = Create("<ListBucketResult></ListBucketResult>");

        _ = client.ListObjects("cold", "photos/2026/");

        Assert.That(handler.LastRequest!.RequestUri!.PathAndQuery,
            Is.EqualTo("/cold?delimiter=%2F&list-type=2&prefix=photos%2F2026%2F"));
    }

    // ---- signing ------------------------------------------------------------

    [Test]
    public void The_signed_headers_are_all_present()
    {
        var (client, handler) = Create(BucketsXml);

        _ = client.ListBuckets();

        Assert.That(handler.HeaderValues("x-amz-date").Single(), Is.EqualTo("20260928T120000Z"));
        Assert.That(handler.HeaderValues("x-amz-content-sha256").Single(), Is.EqualTo("UNSIGNED-PAYLOAD"));
        Assert.That(handler.LastRequest!.Headers.Host, Is.EqualTo("rgw.test:7481"));
    }

    [Test]
    public void The_authorization_header_names_the_credential_scope_and_signed_headers()
    {
        var (client, handler) = Create(BucketsXml);

        _ = client.ListBuckets();

        var authorization = handler.HeaderValues("Authorization").Single();
        Assert.That(authorization, Does.StartWith("AWS4-HMAC-SHA256 "));
        Assert.That(authorization, Does.Contain("Credential=coldceph/20260928/us-east-1/s3/aws4_request"));
        Assert.That(authorization, Does.Contain("SignedHeaders=host;x-amz-content-sha256;x-amz-date"));
        Assert.That(Signature(authorization), Has.Length.EqualTo(64));
        Assert.That(Signature(authorization), Does.Match("^[0-9a-f]{64}$"));
    }

    [Test]
    public void The_signature_is_stable_for_the_same_request_at_the_same_moment()
    {
        var (first, firstHandler) = Create(BucketsXml);
        var (second, secondHandler) = Create(BucketsXml);

        _ = first.ListBuckets();
        _ = second.ListBuckets();

        Assert.That(
            Signature(firstHandler.HeaderValues("Authorization").Single()),
            Is.EqualTo(Signature(secondHandler.HeaderValues("Authorization").Single())));
    }

    /// <summary>
    /// Everything below is one property: the signature must actually depend on what is being
    /// signed. A signer that ignored an input would produce identical signatures here, and no
    /// assertion about the header's shape would notice.
    /// </summary>
    [Test]
    public void The_signature_depends_on_the_path()
    {
        Assert.That(SignatureFor(client => client.OpenObject("cold", "a.txt")),
            Is.Not.EqualTo(SignatureFor(client => client.OpenObject("cold", "b.txt"))));
    }

    [Test]
    public void The_signature_depends_on_the_bucket()
    {
        Assert.That(SignatureFor(client => client.OpenObject("cold", "a.txt")),
            Is.Not.EqualTo(SignatureFor(client => client.OpenObject("warm", "a.txt"))));
    }

    [Test]
    public void The_signature_depends_on_the_query()
    {
        Assert.That(SignatureFor(client => client.ListObjects("cold", "one/")),
            Is.Not.EqualTo(SignatureFor(client => client.ListObjects("cold", "two/"))));
    }

    [Test]
    public void The_signature_depends_on_the_method()
    {
        var get = SignatureFor(client => client.OpenObject("cold", "a.txt"));
        var put = SignatureFor(client =>
            client.PutObject("cold", "a.txt", new MemoryStream(Encoding.UTF8.GetBytes("x")), "text/plain"));

        Assert.That(get, Is.Not.EqualTo(put));
    }

    [Test]
    public void The_signature_depends_on_the_secret_key()
    {
        Assert.That(
            SignatureFor(client => client.ListBuckets(), secret: "one"),
            Is.Not.EqualTo(SignatureFor(client => client.ListBuckets(), secret: "two")));
    }

    [Test]
    public void The_signature_depends_on_the_region()
    {
        Assert.That(
            SignatureFor(client => client.ListBuckets(), region: "us-east-1"),
            Is.Not.EqualTo(SignatureFor(client => client.ListBuckets(), region: "eu-west-2")));
    }

    [Test]
    public void The_signature_depends_on_the_timestamp()
    {
        Assert.That(
            SignatureFor(client => client.ListBuckets(), at: new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero)),
            Is.Not.EqualTo(SignatureFor(client => client.ListBuckets(), at: new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero))));
    }

    [Test]
    public void The_access_key_is_not_the_secret_key()
    {
        var (client, handler) = Create(BucketsXml, secret: "the-secret");

        _ = client.ListBuckets();

        var authorization = handler.HeaderValues("Authorization").Single();
        Assert.That(authorization, Does.Contain("Credential=coldceph/"));
        Assert.That(authorization, Does.Not.Contain("the-secret"));
    }

    // ---- failures -----------------------------------------------------------

    [TestCase(HttpStatusCode.Forbidden)]
    [TestCase(HttpStatusCode.NotFound)]
    [TestCase(HttpStatusCode.InternalServerError)]
    public void A_failed_request_throws_with_the_status(HttpStatusCode status)
    {
        var handler = StubHttpHandler.Returning(status, "<Error><Code>SignatureDoesNotMatch</Code></Error>", "application/xml");
        var client = Build(handler);

        Assert.That(() => client.ListBuckets(),
            Throws.InvalidOperationException.With.Message.Contains(((int)status).ToString()));
    }

    [Test]
    public void A_transport_failure_propagates()
    {
        var handler = new StubHttpHandler(_ => throw new HttpRequestException("connection refused"));
        var client = Build(handler);

        Assert.That(() => client.ListBuckets(), Throws.Exception);
    }

    private static string Signature(string authorization)
        => authorization.Split("Signature=", StringSplitOptions.None).Last().Trim();

    private static string SignatureFor(
        Action<RgwS3Client> act,
        string secret = "coldcephsecret",
        string region = "us-east-1",
        DateTimeOffset? at = null)
    {
        var (client, handler) = Create(BucketsXml, secret, region, at);
        try
        {
            act(client);
        }
        catch (Exception)
        {
            // Some calls parse the canned body and fail; the signature is already on the wire.
        }

        return Signature(handler.HeaderValues("Authorization").Single());
    }

    private static (RgwS3Client Client, StubHttpHandler Handler) Create(
        string body,
        string secret = "coldcephsecret",
        string region = "us-east-1",
        DateTimeOffset? at = null)
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, body, "application/xml");
        return (Build(handler, secret, region, at), handler);
    }

    private static RgwS3Client Build(
        StubHttpHandler handler,
        string secret = "coldcephsecret",
        string region = "us-east-1",
        DateTimeOffset? at = null)
        => new(
            handler.AsFactory(),
            new ControlConfig
            {
                RgwEndpoint = new Uri("http://rgw.test:7481"),
                S3AccessKey = "coldceph",
                S3SecretKey = secret,
                S3Region = region
            },
            new FakeClock { UtcNow = at ?? new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero) });
}

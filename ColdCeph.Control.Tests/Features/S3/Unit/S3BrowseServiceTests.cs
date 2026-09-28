using ColdCeph.Control.Features.S3.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ColdCeph.Control.Tests.Features.S3.Unit;

[TestFixture]
public sealed class S3BrowseServiceTests
{
    [Test]
    public void ListBuckets_refuses_when_storage_is_not_ready()
    {
        using var factory = new Support.ControlAppFactory();
        factory.Rgw.SeedBucket("cold");
        var browse = factory.Services.GetRequiredService<S3BrowseService>();

        Assert.That(() => browse.ListBuckets(), Throws.InvalidOperationException);
        Assert.That(browse.CanBrowse(), Is.False);
    }

    [Test]
    public void Page_does_not_list_buckets_when_storage_is_asleep()
    {
        using var factory = new Support.ControlAppFactory();
        factory.Rgw.SeedBucket("cold");
        var browse = factory.Services.GetRequiredService<S3BrowseService>();

        var model = browse.Page(null, "");

        Assert.That(model.BrowseReady, Is.False);
        Assert.That(model.Buckets, Is.Empty);
        Assert.That(model.Banner, Does.Contain("Finish setting up storage"));
    }
}

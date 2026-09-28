using ColdCeph.Debug.Features.Pages.Services;

namespace ColdCeph.Debug.Tests.Features.Pages.Unit;

[TestFixture]
public sealed class OperatorPageCatalogTests
{
    [Test]
    public void Catalog_lists_operator_html_surfaces()
    {
        var catalog = new OperatorPageCatalog();

        Assert.That(catalog.All.Select(page => page.Path), Does.Contain("/"));
        Assert.That(catalog.All.Select(page => page.Path), Does.Contain("/hosts"));
        Assert.That(catalog.All.Select(page => page.Path), Does.Contain("/auth/login"));
        Assert.That(catalog.All.Select(page => page.Path), Does.Contain("/s3"));
        Assert.That(catalog.Resolve("/s3").Name, Is.EqualTo("Buckets"));
        Assert.That(() => catalog.Resolve("/integrity"), Throws.ArgumentException);
    }

    [Test]
    public void Catalog_refuses_the_json_api()
    {
        var catalog = new OperatorPageCatalog();

        Assert.That(() => catalog.Resolve("/v1/hosts"), Throws.ArgumentException);
        Assert.That(catalog.All.Select(page => page.Path), Does.Not.Contain("/v1"));
    }
}

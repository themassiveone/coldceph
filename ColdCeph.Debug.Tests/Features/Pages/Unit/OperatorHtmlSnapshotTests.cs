using ColdCeph.Debug.Features.Pages.Providers;

namespace ColdCeph.Debug.Tests.Features.Pages.Unit;

[TestFixture]
public sealed class OperatorHtmlSnapshotTests
{
    [Test]
    public void Inlines_the_operator_stylesheet()
    {
        var html = """<link rel="stylesheet" href="/css/operator.css" />""";

        var styled = OperatorHtmlSnapshot.InlineCss(html, "body{color:red}");

        Assert.That(styled, Does.Contain("<style>body{color:red}</style>"));
        Assert.That(styled, Does.Not.Contain("href=\"/css/operator.css\""));
    }

    [Test]
    public void Refuses_html_without_the_operator_stylesheet()
    {
        Assert.That(
            () => OperatorHtmlSnapshot.InlineCss("<h1>no css</h1>", "body{}"),
            Throws.InvalidOperationException);
    }
}

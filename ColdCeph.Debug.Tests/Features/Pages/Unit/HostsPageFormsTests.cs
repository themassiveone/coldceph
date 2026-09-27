using ColdCeph.Debug.Features.Pages.Services;

namespace ColdCeph.Debug.Tests.Features.Pages.Unit;

[TestFixture]
public sealed class HostsPageFormsTests
{
    [Test]
    public void Reads_pending_approve_host_ids()
    {
        var html = """
            <form method="post" action="/hosts/node-a/approve"></form>
            <form method="post" action="/hosts/node-b/approve"></form>
            """;

        var ids = HostsPageForms.PendingApproveIds(html);

        Assert.That(ids, Is.EqualTo(new[] { "node-a", "node-b" }));
    }

    [Test]
    public void Pending_approve_ids_are_empty_when_nothing_is_waiting()
    {
        var ids = HostsPageForms.PendingApproveIds("<p class=\"cc-empty\">No join requests</p>");

        Assert.That(ids, Is.Empty);
    }

    [Test]
    public void Reads_the_antiforgery_token()
    {
        var html = """<input name="__RequestVerificationToken" type="hidden" value="tok-1" />""";

        Assert.That(HostsPageForms.AntiforgeryToken(html), Is.EqualTo("tok-1"));
    }

    [Test]
    public void Token_required()
    {
        Assert.That(() => HostsPageForms.AntiforgeryToken("<html></html>"), Throws.InvalidOperationException);
    }
}

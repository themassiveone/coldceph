namespace ColdCeph.Control.Tests.Features.Integrity.HTTP;

[TestFixture]
public sealed class IntegrityPagesHttpTests
{
    [Test]
    public async Task Integrity_keeps_ceph_health_distinct_from_cold_storage()
    {
        using var factory = new Support.ControlAppFactory();
        using var client = await Support.OperatorClient.SignedIn(factory);

        var html = await client.GetStringAsync("/integrity");

        Assert.That(html, Does.Contain("Cold-storage"));
        Assert.That(html, Does.Contain("Raw Ceph health:"));
        Assert.That(html, Does.Not.Contain("http-equiv=\"refresh\""));
        Assert.That(factory.Ceph.HealthDetailCalls, Is.GreaterThan(0));
    }

    [Test]
    public async Task Integrity_does_not_replace_raw_ceph_status()
    {
        using var factory = new Support.ControlAppFactory();
        using var client = await Support.OperatorClient.SignedIn(factory);

        var html = await client.GetStringAsync("/integrity");

        Assert.That(html, Does.Contain("Write ready"));
        Assert.That(html, Does.Contain("Ceph health"));
        Assert.That(html, Does.Not.Contain("Wake disks"));
    }

    [Test]
    public async Task Integrity_page_renders_when_ceph_is_down()
    {
        using var factory = new Support.ControlAppFactory();
        factory.Ceph.ThrowOnHealth = true;
        using var client = await Support.OperatorClient.SignedIn(factory);

        var html = await client.GetStringAsync("/integrity");

        Assert.That(html, Does.Contain("UNAVAILABLE"));
        Assert.That(html, Does.Not.Contain("HEALTH_OK"));
    }
}

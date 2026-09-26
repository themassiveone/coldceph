using ColdCeph.Control.Composition;

namespace ColdCeph.Control.Tests.Features.Hosts.Unit;

[TestFixture]
public sealed class ControlConfigListenUrlsTests
{
    [Test]
    public void ListenUrls_binds_operator_and_s3_on_every_interface()
    {
        var config = new ControlConfig { OperatorPort = 8080, S3Port = 7480 };

        var urls = config.ListenUrls();

        Assert.That(urls, Does.Contain("http://*:8080"));
        Assert.That(urls, Does.Contain("http://*:7480"));
    }

    [Test]
    public void ListenUrls_does_not_pin_loopback_only()
    {
        var config = new ControlConfig { OperatorPort = 9090, S3Port = 9191 };

        var urls = config.ListenUrls();

        Assert.That(urls, Does.Not.Contain("http://127.0.0.1:9090"));
        Assert.That(urls, Does.Not.Contain("http://*:8080"));
        Assert.That(urls, Does.Not.Contain("http://*:7480"));
    }
}

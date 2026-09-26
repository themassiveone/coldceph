using ColdCeph.Node.Composition;
using Microsoft.Extensions.Logging;

namespace ColdCeph.Node.Tests.Features.Hosts.Unit;

[TestFixture]
public sealed class NodeLoggingTests
{
    [Test]
    public void ShouldLog_hides_debug_and_httpclient_information()
    {
        Assert.That(NodeLogging.ShouldLog("Microsoft.AspNetCore.Server.Kestrel", LogLevel.Debug), Is.False);
        Assert.That(NodeLogging.ShouldLog("System.Net.Http.HttpClient.control.LogicalHandler", LogLevel.Information), Is.False);
    }

    [Test]
    public void ShouldLog_keeps_hosting_information()
    {
        Assert.That(NodeLogging.ShouldLog("Microsoft.Hosting.Lifetime", LogLevel.Information), Is.True);
        Assert.That(NodeLogging.ShouldLog("System.Net.Http.HttpClient.control.LogicalHandler", LogLevel.Warning), Is.True);
    }
}

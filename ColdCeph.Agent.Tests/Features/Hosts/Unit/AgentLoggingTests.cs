using ColdCeph.Agent.Composition;
using Microsoft.Extensions.Logging;

namespace ColdCeph.Agent.Tests.Features.Hosts.Unit;

[TestFixture]
public sealed class AgentLoggingTests
{
    [Test]
    public void ShouldLog_hides_debug_and_httpclient_information()
    {
        Assert.That(AgentLogging.ShouldLog("Microsoft.AspNetCore.Server.Kestrel", LogLevel.Debug), Is.False);
        Assert.That(AgentLogging.ShouldLog("System.Net.Http.HttpClient.control.LogicalHandler", LogLevel.Information), Is.False);
    }

    [Test]
    public void ShouldLog_keeps_hosting_information()
    {
        Assert.That(AgentLogging.ShouldLog("Microsoft.Hosting.Lifetime", LogLevel.Information), Is.True);
        Assert.That(AgentLogging.ShouldLog("System.Net.Http.HttpClient.control.LogicalHandler", LogLevel.Warning), Is.True);
    }
}

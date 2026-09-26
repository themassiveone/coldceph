using ColdCeph.Control.Composition;
using Microsoft.Extensions.Logging;

namespace ColdCeph.Control.Tests.Features.Hosts.Unit;

[TestFixture]
public sealed class ControlLoggingTests
{
    [Test]
    public void ShouldLog_hides_httpclient_information()
    {
        Assert.That(ControlLogging.ShouldLog("System.Net.Http.HttpClient.node.LogicalHandler", LogLevel.Information), Is.False);
        Assert.That(ControlLogging.ShouldLog("System.Net.Http.HttpClient.node.ClientHandler", LogLevel.Debug), Is.False);
    }

    [Test]
    public void ShouldLog_keeps_warnings_and_non_httpclient_information()
    {
        Assert.That(ControlLogging.ShouldLog("System.Net.Http.HttpClient.node.LogicalHandler", LogLevel.Warning), Is.True);
        Assert.That(ControlLogging.ShouldLog("Microsoft.Hosting.Lifetime", LogLevel.Information), Is.True);
        Assert.That(ControlLogging.ShouldLog("Microsoft.AspNetCore.Mvc.Razor.Compilation.DefaultViewCompiler", LogLevel.Debug), Is.False);
    }
}

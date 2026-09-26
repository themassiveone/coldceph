using ColdCeph.Debug.Features.Host.Providers;

namespace ColdCeph.Debug.Tests.Features.Host.Unit;

[TestFixture]
public sealed class DebugArgParserTests
{
    [Test]
    public void Parses_screenshot_of_one_page()
    {
        var command = new DebugArgParser().Parse(["screenshot", "/hosts"]);

        Assert.That(command.Verb, Is.EqualTo("screenshot"));
        Assert.That(command.PagePath, Is.EqualTo("/hosts"));
    }

    [Test]
    public void No_args_captures_every_operator_page()
    {
        var command = new DebugArgParser().Parse([]);

        Assert.That(command.Verb, Is.EqualTo("screenshot"));
        Assert.That(command.PagePath, Is.Null);
    }

    [Test]
    public void Rejects_unknown_verbs()
    {
        Assert.That(() => new DebugArgParser().Parse(["up"]), Throws.ArgumentException);
        Assert.That(() => new DebugArgParser().Parse(["screenshot", "/hosts", "/osds"]), Throws.ArgumentException);
    }
}

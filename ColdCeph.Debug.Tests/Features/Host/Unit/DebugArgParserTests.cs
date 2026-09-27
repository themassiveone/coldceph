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
    public void Parses_compose_up()
    {
        var command = new DebugArgParser().Parse(["up"]);

        Assert.That(command.Verb, Is.EqualTo("up"));
        Assert.That(command.PagePath, Is.Null);
    }

    [Test]
    public void Parses_down_and_status()
    {
        Assert.That(new DebugArgParser().Parse(["down"]).Verb, Is.EqualTo("down"));
        Assert.That(new DebugArgParser().Parse(["status"]).Verb, Is.EqualTo("status"));
    }

    [Test]
    public void Parses_allow_all_pending_and_one_host()
    {
        var all = new DebugArgParser().Parse(["allow"]);
        var one = new DebugArgParser().Parse(["allow", "node-a"]);

        Assert.That(all.Verb, Is.EqualTo("allow"));
        Assert.That(all.HostId, Is.Null);
        Assert.That(one.HostId, Is.EqualTo("node-a"));
    }

    [Test]
    public void Rejects_allow_with_extra_arguments()
    {
        Assert.That(() => new DebugArgParser().Parse(["allow", "node-a", "node-b"]), Throws.ArgumentException);
    }

    [Test]
    public void Rejects_unknown_verbs()
    {
        Assert.That(() => new DebugArgParser().Parse(["launch"]), Throws.ArgumentException);
        Assert.That(() => new DebugArgParser().Parse(["nodes"]), Throws.ArgumentException);
        Assert.That(() => new DebugArgParser().Parse(["screenshot", "/hosts", "/osds"]), Throws.ArgumentException);
    }

    [Test]
    public void Rejects_up_arguments()
    {
        Assert.That(() => new DebugArgParser().Parse(["up", "--build"]), Throws.ArgumentException);
        Assert.That(() => new DebugArgParser().Parse(["up", "--no-deps"]), Throws.ArgumentException);
        Assert.That(() => new DebugArgParser().Parse(["down", "--build"]), Throws.ArgumentException);
    }
}

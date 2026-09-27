using ColdCeph.Debug.Features.Compose.Interfaces;
using ColdCeph.Debug.Features.Compose.Services;

namespace ColdCeph.Debug.Tests.Features.Compose.Unit;

[TestFixture]
public sealed class ComposeStackServiceTests
{
    [Test]
    public void Up_waits_for_the_compose_file_and_starts_control_when_it_is_down()
    {
        var compose = new RecordingComposeCli();
        var health = new StubControlHealth(ready: false);
        var control = new RecordingControlProcess();
        var stack = new ComposeStackService(compose, health, control);

        stack.Up(rebuildImages: false);

        Assert.That(compose.Calls, Has.Count.EqualTo(1));
        Assert.That(compose.Calls[0], Is.EqualTo(new[] { "up", "-d", "--wait" }));
        Assert.That(control.Starts, Is.EqualTo(1));
    }

    [Test]
    public void Up_does_not_start_control_when_health_is_already_ready()
    {
        var compose = new RecordingComposeCli();
        var control = new RecordingControlProcess();
        var stack = new ComposeStackService(compose, new StubControlHealth(ready: true), control);

        stack.Up(rebuildImages: false);

        Assert.That(compose.Calls[0], Is.EqualTo(new[] { "up", "-d", "--wait" }));
        Assert.That(control.Starts, Is.EqualTo(0));
    }

    [Test]
    public void Up_rebuilds_images_only_when_requested()
    {
        var compose = new RecordingComposeCli();
        var stack = new ComposeStackService(compose, new StubControlHealth(ready: true), new RecordingControlProcess());

        stack.Up(rebuildImages: true);

        Assert.That(compose.Calls[0], Is.EqualTo(new[] { "up", "-d", "--build", "--wait" }));
    }

    [Test]
    public void Up_does_not_start_control_when_compose_fails()
    {
        var compose = new RecordingComposeCli { ThrowOnRun = true };
        var control = new RecordingControlProcess();
        var stack = new ComposeStackService(compose, new StubControlHealth(ready: false), control);

        Assert.That(() => stack.Up(rebuildImages: false), Throws.InvalidOperationException);
        Assert.That(control.Starts, Is.EqualTo(0));
    }

    [Test]
    public void Down_stops_compose_and_owned_control()
    {
        var compose = new RecordingComposeCli();
        var control = new RecordingControlProcess { Owned = true };
        var stack = new ComposeStackService(compose, new StubControlHealth(ready: true), control);

        stack.Down();

        Assert.That(compose.Calls, Has.Count.EqualTo(1));
        Assert.That(compose.Calls[0], Is.EqualTo(new[] { "down" }));
        Assert.That(control.Stops, Is.EqualTo(1));
    }

    [Test]
    public void Down_leaves_control_alone_when_this_cli_did_not_start_it()
    {
        var control = new RecordingControlProcess { Owned = false };
        var stack = new ComposeStackService(new RecordingComposeCli(), new StubControlHealth(ready: true), control);

        stack.Down();

        Assert.That(control.Stops, Is.EqualTo(0));
    }

    [Test]
    public void Status_asks_compose_for_ps_without_mutating()
    {
        var compose = new RecordingComposeCli { Output = "cc-a Up" };
        var control = new RecordingControlProcess();
        var stack = new ComposeStackService(compose, new StubControlHealth(ready: true), control);

        var status = stack.Status();

        Assert.That(compose.Calls[0], Is.EqualTo(new[] { "ps", "-a" }));
        Assert.That(status, Does.Contain("cc-a Up"));
        Assert.That(status, Does.Contain("control: ready"));
        Assert.That(control.Starts, Is.EqualTo(0));
        Assert.That(control.Stops, Is.EqualTo(0));
    }

    [Test]
    public void Status_reports_when_control_is_not_ready()
    {
        var stack = new ComposeStackService(
            new RecordingComposeCli { Output = "" },
            new StubControlHealth(ready: false),
            new RecordingControlProcess());

        Assert.That(stack.Status(), Does.Contain("control: not ready"));
    }

    private sealed class RecordingComposeCli : IComposeCli
    {
        public List<string[]> Calls { get; } = [];
        public string Output { get; init; } = "";
        public bool ThrowOnRun { get; init; }

        public string Run(params string[] arguments)
        {
            Calls.Add(arguments);
            if (ThrowOnRun)
                throw new InvalidOperationException("compose failed");
            return Output;
        }
    }

    private sealed class StubControlHealth(bool ready) : IControlHealth
    {
        public bool IsReady() => ready;
    }

    private sealed class RecordingControlProcess : IControlProcess
    {
        public int Starts { get; private set; }
        public int Stops { get; private set; }
        public bool Owned { get; set; }

        public void Start() => Starts++;

        public void Stop() => Stops++;
    }
}

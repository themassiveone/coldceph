using ColdCeph.Core.Features.Devices.DTOs;
using ColdCeph.Node.Features.Devices.Providers;
using ColdCeph.Node.Shared;

namespace ColdCeph.Node.Tests.Features.Devices.Provider;

/// <summary>
/// The provider that physically spins ColdCeph's drives up and down had no tests, while its
/// in-memory stand-in had a fixture of its own. It is also bypassed in local development and in
/// E2E, so before these it first ran on a real appliance.
/// <para>
/// Output strings here are <c>hdparm -C</c>'s actual wording.
/// </para>
/// </summary>
[TestFixture]
public sealed class HdparmDiskPowerTests
{
    // ---- reading state ------------------------------------------------------

    [TestCase("\n/dev/sda:\n drive state is:  active/idle\n", DevicePowerState.Active)]
    [TestCase("\n/dev/sda:\n drive state is:  standby\n", DevicePowerState.Standby)]
    [TestCase("\n/dev/sda:\n drive state is:  idle\n", DevicePowerState.Active)]
    public void Recognised_states_are_mapped(string output, DevicePowerState expected)
    {
        var runner = new RecordingRunner { Output = output };
        var power = new HdparmDiskPower(runner);

        Assert.That(power.GetPowerState("d0", "/dev/sda"), Is.EqualTo(expected));
    }

    /// <summary>
    /// A sleeping drive has its platters stopped, so it counts as standby. Reading it as Active —
    /// which anything-not-"standby" did — meant "every device is in standby" could never become
    /// true and the appliance never finished going to sleep.
    /// </summary>
    [Test]
    public void A_sleeping_drive_is_standby_not_active()
    {
        var runner = new RecordingRunner { Output = "\n/dev/sda:\n drive state is:  sleeping\n" };
        var power = new HdparmDiskPower(runner);

        Assert.That(power.GetPowerState("d0", "/dev/sda"), Is.EqualTo(DevicePowerState.Standby));
        Assert.That(power.GetPowerState("d0", "/dev/sda"), Is.Not.EqualTo(DevicePowerState.Active));
    }

    /// <summary>
    /// Unknown must not be Active either: guessing Active means the plane concludes a spun-down
    /// disk is awake and starts an OSD on it.
    /// </summary>
    [TestCase("\n/dev/sda:\n drive state is:  unknown\n")]
    [TestCase("SG_IO: bad/missing sense data, sb[]:  70 00 05 00\n")]
    [TestCase("")]
    [TestCase("garbage that is not hdparm output at all")]
    public void Unreadable_states_are_unknown(string output)
    {
        var runner = new RecordingRunner { Output = output };
        var power = new HdparmDiskPower(runner);

        var state = power.GetPowerState("d0", "/dev/sda");

        Assert.That(state, Is.EqualTo(DevicePowerState.Unknown));
        Assert.That(state, Is.Not.EqualTo(DevicePowerState.Active));
        Assert.That(state, Is.Not.EqualTo(DevicePowerState.Standby));
    }

    [Test]
    public void A_failing_hdparm_is_unknown()
    {
        var runner = new RecordingRunner { Throw = true };
        var power = new HdparmDiskPower(runner);

        Assert.That(power.GetPowerState("d0", "/dev/sda"), Is.EqualTo(DevicePowerState.Unknown));
    }

    [Test]
    public void A_timed_out_hdparm_is_unknown_rather_than_propagating()
    {
        var runner = new RecordingRunner { ThrowTimeout = true };
        var power = new HdparmDiskPower(runner);

        Assert.That(power.GetPowerState("d0", "/dev/sda"), Is.EqualTo(DevicePowerState.Unknown));
    }

    [Test]
    public void A_device_with_no_path_is_unknown_and_runs_nothing()
    {
        var runner = new RecordingRunner();
        var power = new HdparmDiskPower(runner);

        Assert.That(power.GetPowerState("d0", null), Is.EqualTo(DevicePowerState.Unknown));
        Assert.That(runner.Commands, Is.Empty);
    }

    [Test]
    public void Reading_state_uses_the_capability_query()
    {
        var runner = new RecordingRunner { Output = "\n/dev/sda:\n drive state is:  standby\n" };
        var power = new HdparmDiskPower(runner);

        _ = power.GetPowerState("d0", "/dev/sda");

        Assert.That(runner.Commands, Is.EqualTo(new[] { "hdparm -C /dev/sda" }));
    }

    // ---- changing state -----------------------------------------------------

    /// <summary>
    /// A drive spins up on I/O, not on a configuration change. <c>hdparm -S 0</c> only clears the
    /// spindown timer, so a wake that issued only that left the drive standing by — while Control
    /// recorded it as awake and started an OSD on it.
    /// </summary>
    [Test]
    public void Waking_forces_a_read_rather_than_only_clearing_the_spindown_timer()
    {
        var runner = new RecordingRunner();
        var power = new HdparmDiskPower(runner);

        power.Wake("/dev/sda");

        Assert.That(runner.Commands, Does.Contain("hdparm --read-sector 0 /dev/sda"));
        Assert.That(runner.Commands.First(), Is.EqualTo("hdparm --read-sector 0 /dev/sda"),
            "the spin-up has to come first, or the timer is cleared on a drive that is still parked");
    }

    [Test]
    public void Waking_also_clears_the_spindown_timer()
    {
        var runner = new RecordingRunner();
        var power = new HdparmDiskPower(runner);

        power.Wake("/dev/sda");

        Assert.That(runner.Commands, Does.Contain("hdparm -S 0 /dev/sda"));
    }

    [Test]
    public void Standby_parks_the_drive_immediately()
    {
        var runner = new RecordingRunner();
        var power = new HdparmDiskPower(runner);

        power.Standby("/dev/sda");

        Assert.That(runner.Commands, Is.EqualTo(new[] { "hdparm -y /dev/sda" }));
    }

    [Test]
    public void Neither_wake_nor_standby_runs_anything_without_a_path()
    {
        var runner = new RecordingRunner();
        var power = new HdparmDiskPower(runner);

        power.Wake(null);
        power.Standby(null);

        Assert.That(runner.Commands, Is.Empty);
    }

    /// <summary>
    /// A failed wake or standby must reach the caller: Control has to hear that the drive did not
    /// change state rather than record the state it asked for.
    /// </summary>
    [Test]
    public void A_failing_wake_propagates()
    {
        var runner = new RecordingRunner { Throw = true };
        var power = new HdparmDiskPower(runner);

        Assert.That(() => power.Wake("/dev/sda"), Throws.InvalidOperationException);
    }

    [Test]
    public void A_failing_standby_propagates()
    {
        var runner = new RecordingRunner { Throw = true };
        var power = new HdparmDiskPower(runner);

        Assert.That(() => power.Standby("/dev/sda"), Throws.InvalidOperationException);
    }

    [Test]
    public void Nothing_here_destroys_data()
    {
        var runner = new RecordingRunner { Output = "\n/dev/sda:\n drive state is:  standby\n" };
        var power = new HdparmDiskPower(runner);

        _ = power.GetPowerState("d0", "/dev/sda");
        power.Wake("/dev/sda");
        power.Standby("/dev/sda");

        foreach (var dangerous in new[] { "--security-erase", "--trim-sector-ranges", "--write-sector", "--make-bad-sector" })
            Assert.That(runner.Commands, Has.None.Contains(dangerous));
    }

    private sealed class RecordingRunner : IProcessRunner
    {
        public List<string> Commands { get; } = [];
        public string Output { get; set; } = "";
        public bool Throw { get; set; }
        public bool ThrowTimeout { get; set; }

        public string Run(string fileName, IReadOnlyList<string> arguments)
        {
            Commands.Add($"{fileName} {string.Join(' ', arguments)}");
            if (ThrowTimeout)
                throw new TimeoutException("hdparm did not exit within the timeout.");
            if (Throw)
                throw new InvalidOperationException("hdparm exited 1: Operation not permitted");
            return Output;
        }
    }
}

using ColdCeph.Control.Tests.Support;
using ColdCeph.Core.Features.Devices.DTOs;
using ColdCeph.Core.Features.Osds.DTOs;
using ColdCeph.Core.Features.StoragePlane.DTOs;

namespace ColdCeph.Control.Tests.Features.Devices.Unit;

[TestFixture]
public sealed class DevicesReconcilerTests
{
    // ---- wake ---------------------------------------------------------------

    /// <summary>
    /// Devices watches StoragePlane and issues the wake itself; StoragePlane never calls into it.
    /// </summary>
    [Test]
    public void Waking_issues_disk_wake_from_the_devices_slice()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.Wake();

        harness.DevicesLoop.ReconcileOnce();

        Assert.That(harness.Log.Commands, Does.Contain("7080 wake d0"));
    }

    [Test]
    public void Waking_wakes_disks_on_every_enrolled_host()
    {
        var harness = TwoHosts();
        harness.Wake();

        harness.DevicesLoop.ReconcileOnce();

        Assert.That(harness.Log.Commands, Does.Contain("7081 wake d0"));
        Assert.That(harness.Log.Commands, Does.Contain("7082 wake d1"));
        Assert.That(harness.Log.Commands, Does.Not.Contain("7081 wake d1"));
    }

    [Test]
    public void A_cold_plane_wakes_nothing()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.Plane.MarkObserved(StoragePlaneState.Cold, "startup-reconcile");

        harness.DevicesLoop.ReconcileOnce();

        Assert.That(harness.Log.Commands, Is.Empty);
    }

    [Test]
    public void A_disk_already_active_is_not_woken_again()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.Wake();

        harness.Tick(8);

        Assert.That(harness.Log.CountOf("wake d0"), Is.EqualTo(1));
    }

    [Test]
    public void A_disk_that_has_not_spun_up_is_retried_once_per_tick()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.NodeDevices.WakeDoesNotTake = true;
        harness.Wake();

        harness.Tick(4);

        Assert.That(harness.Log.CountOf("wake d0"), Is.EqualTo(4));
    }

    // ---- standby, and the invariant that guards it --------------------------

    /// <summary>
    /// The core safety invariant: a drive never spins down under a running OSD.
    /// </summary>
    [Test]
    public void Sleeping_does_not_standby_while_an_osd_still_runs()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.NodeOsds.StopDoesNotTake = true;
        harness.Wake();
        harness.Tick(2);
        harness.Confirm();
        harness.Clock.UtcNow += TimeSpan.FromMinutes(16);

        harness.Tick(5);

        Assert.That(harness.Osds.IsEveryProcessStopped(), Is.False);
        Assert.That(harness.Log.CountOf("standby d0"), Is.EqualTo(0));
    }

    [Test]
    public void Sleeping_stands_the_disk_down_once_the_osd_has_stopped()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.Wake();
        harness.Tick(2);
        harness.Confirm();
        harness.Clock.UtcNow += TimeSpan.FromMinutes(16);

        harness.Tick(5);

        Assert.That(harness.Log.CountOf("standby d0"), Is.EqualTo(1));
        Assert.That(harness.Log.IndexOf("standby d0"), Is.GreaterThan(harness.Log.IndexOf("stop 0")));
        Assert.That(harness.Devices.IsEveryDeviceStandby(), Is.True);
    }

    // ---- fail closed --------------------------------------------------------

    /// <summary>
    /// An unreachable node must leave the disk recorded as awake. Recording the requested state
    /// let a missing node take the plane to COLD with the platters still spinning.
    /// </summary>
    [Test]
    public void An_unreachable_node_leaves_the_disk_recorded_as_awake()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.Wake();
        harness.Tick(2);
        harness.Confirm();
        harness.Clock.UtcNow += TimeSpan.FromMinutes(16);
        harness.Tick(1);
        harness.NodeDevices.StandbyDoesNotTake = true;

        harness.Tick(3);

        Assert.That(harness.Devices.IsEveryDeviceStandby(), Is.False);
        Assert.That(harness.State, Is.Not.EqualTo(StoragePlaneState.Cold));
    }

    [Test]
    public void One_host_failing_still_wakes_the_others()
    {
        var harness = TwoHosts();
        harness.NodeDevices.ThrowOnWakeFor.Add(new Uri("http://127.0.0.1:7081"));
        harness.Wake();

        harness.DevicesLoop.ReconcileOnce();

        Assert.That(harness.Log.Commands, Does.Contain("7082 wake d1"));
        Assert.That(harness.Log.Commands, Does.Not.Contain("7081 wake d0"));
    }

    [Test]
    public void A_node_failure_does_not_escape_the_tick()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.NodeDevices.ThrowOnWake = true;
        harness.Wake();

        Assert.DoesNotThrow(() => harness.DevicesLoop.ReconcileOnce());
        Assert.That(harness.Log.Commands, Is.Empty);
    }

    [Test]
    public void A_node_failure_does_not_prevent_a_later_wake()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.NodeDevices.ThrowOnWake = true;
        harness.Wake();
        harness.DevicesLoop.ReconcileOnce();

        harness.NodeDevices.ThrowOnWake = false;
        harness.DevicesLoop.ReconcileOnce();

        Assert.That(harness.Log.Commands, Does.Contain("7080 wake d0"));
    }

    [Test]
    public void An_unreachable_node_is_recorded()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.NodeDevices.ThrowOnWake = true;
        harness.Wake();

        harness.DevicesLoop.ReconcileOnce();

        Assert.That(harness.DevicesLoop.LastError, Does.Contain("h1"));
    }

    // ---- inventory ----------------------------------------------------------

    [Test]
    public void An_error_observation_with_no_devices_does_not_erase_inventory()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();

        harness.Devices.ApplyObserved(new HostDevicesObservationDto
        {
            HostId = "h1",
            Devices = [],
            Error = "hdparm: not permitted"
        });

        Assert.That(harness.Devices.ListDevices(), Has.Count.EqualTo(1));
        Assert.That(harness.Devices.IsEveryDeviceStandby(), Is.True);
        Assert.That(harness.Devices.ListObservationErrors().Single(), Does.Contain("hdparm"));
    }

    [Test]
    public void A_clean_observation_with_no_devices_does_empty_the_host()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();

        harness.Devices.ApplyObserved(new HostDevicesObservationDto
        {
            HostId = "h1",
            Devices = [],
            Error = null
        });

        Assert.That(harness.Devices.ListDevices(), Is.Empty);
    }

    private static PlaneHarness TwoHosts()
    {
        var harness = new PlaneHarness().WithHost("node-a", 7081).WithHost("node-b", 7082);
        harness.Devices.ApplyObserved(new HostDevicesObservationDto
        {
            HostId = "node-a",
            Devices = [Device("d0", "node-a", 0)]
        });
        harness.Devices.ApplyObserved(new HostDevicesObservationDto
        {
            HostId = "node-b",
            Devices = [Device("d1", "node-b", 1)]
        });
        harness.Osds.ApplyObserved(new HostOsdsObservationDto
        {
            HostId = "node-a",
            Osds = [new OsdDto { OsdId = 0, HostId = "node-a", DeviceId = "d0", Up = false, In = true, ProcessRunning = false }]
        });
        harness.Osds.ApplyObserved(new HostOsdsObservationDto
        {
            HostId = "node-b",
            Osds = [new OsdDto { OsdId = 1, HostId = "node-b", DeviceId = "d1", Up = false, In = true, ProcessRunning = false }]
        });
        return harness;
    }

    private static DeviceDto Device(string deviceId, string hostId, int osdId)
        => new()
        {
            DeviceId = deviceId,
            HostId = hostId,
            MappedOsdId = osdId,
            Wwn = $"wwn-{deviceId}",
            Serial = $"serial-{deviceId}",
            Path = $"/dev/{deviceId}",
            PowerState = DevicePowerState.Standby
        };
}

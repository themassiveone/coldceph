using ColdCeph.Control.Tests.Support;
using ColdCeph.Core.Features.Devices.DTOs;
using ColdCeph.Core.Features.Osds.DTOs;
using ColdCeph.Core.Features.StoragePlane.DTOs;

namespace ColdCeph.Control.Tests.Features.Osds.Unit;

[TestFixture]
public sealed class OsdsReconcilerTests
{
    // ---- what it starts, and where ------------------------------------------

    [Test]
    public void Waking_starts_the_osds_a_node_reported()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.Wake();

        harness.Tick(2);

        Assert.That(harness.Log.Commands, Does.Contain("7080 start 0"));
        Assert.That(harness.Osds.IsEveryProcessRunning(), Is.True);
    }

    [Test]
    public void Waking_starts_osds_on_every_enrolled_host()
    {
        var harness = TwoHosts();
        harness.Wake();

        harness.Tick(2);

        Assert.That(harness.Log.Commands, Does.Contain("7081 start 0"));
        Assert.That(harness.Log.Commands, Does.Contain("7082 start 1"));
        Assert.That(harness.Log.Commands, Does.Not.Contain("7081 start 1"));
    }

    [Test]
    public void A_cold_plane_starts_nothing()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.Plane.MarkObserved(StoragePlaneState.Cold, "startup-reconcile");

        harness.Tick(3);

        Assert.That(harness.Log.Commands, Is.Empty);
    }

    [Test]
    public void A_ready_plane_starts_nothing_further()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.Wake();
        harness.Tick(2);
        Assert.That(harness.State, Is.EqualTo(StoragePlaneState.Ready));
        var issued = harness.Log.Commands.Count;

        harness.Tick(5);

        Assert.That(harness.Log.Commands, Has.Count.EqualTo(issued));
    }

    [Test]
    public void Waking_with_no_enrolled_host_starts_nothing()
    {
        var harness = new PlaneHarness().WithColdOsd();
        harness.Wake();

        harness.Tick(3);

        Assert.That(harness.Log.Commands, Is.Empty);
        Assert.That(harness.Osds.IsEveryProcessRunning(), Is.False);
    }

    // ---- no command storm ---------------------------------------------------

    /// <summary>
    /// Both loops used to reissue their command for every OSD and disk on every host on every
    /// tick, for the whole of WAKING — a start and an hdparm wake per second per drive, against
    /// drives that were mid-spin-up. A command is only sent when the observed state differs from
    /// the desired one.
    /// </summary>
    [Test]
    public void An_osd_that_is_already_running_is_not_started_again()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.Wake();

        harness.Tick(10);

        Assert.That(harness.Log.CountOf("start 0"), Is.EqualTo(1));
    }

    /// <summary>
    /// While the OSD genuinely has not come up, retrying is correct — one attempt per tick, not
    /// a burst, and it must not give up.
    /// </summary>
    [Test]
    public void An_osd_that_has_not_come_up_is_retried_once_per_tick()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.NodeOsds.StartDoesNotTake = true;
        harness.Wake();

        harness.Tick(4);

        Assert.That(harness.Log.CountOf("start 0"), Is.EqualTo(4));
        Assert.That(harness.State, Is.EqualTo(StoragePlaneState.Waking));
    }

    [Test]
    public void A_disk_already_in_standby_is_not_told_to_stand_by_again()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.Wake();
        harness.Tick(2);
        harness.Confirm();
        harness.Clock.UtcNow += TimeSpan.FromMinutes(16);

        harness.Tick(8);

        Assert.That(harness.Log.CountOf("standby d0"), Is.EqualTo(1));
        Assert.That(harness.Log.CountOf("stop 0"), Is.EqualTo(1));
    }

    // ---- disk before OSD ----------------------------------------------------

    /// <summary>
    /// The two loops run on independent timers, so nothing sequenced them: an OSD could be
    /// started on a drive that was still spun down. Osds reads Devices' inventory and waits.
    /// </summary>
    [Test]
    public void An_osd_is_not_started_before_its_disk_is_awake()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.NodeDevices.WakeDoesNotTake = true;
        harness.Wake();

        harness.Tick(4);

        Assert.That(harness.Log.CountOf("wake d0"), Is.GreaterThan(0));
        Assert.That(harness.Log.CountOf("start 0"), Is.EqualTo(0));
        Assert.That(harness.State, Is.EqualTo(StoragePlaneState.Waking));
    }

    [Test]
    public void The_disk_wake_precedes_the_osd_start()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.Wake();

        harness.Tick(3);

        Assert.That(harness.Log.IndexOf("wake d0"), Is.GreaterThanOrEqualTo(0));
        Assert.That(harness.Log.IndexOf("start 0"), Is.GreaterThan(harness.Log.IndexOf("wake d0")));
    }

    [Test]
    public void An_osd_whose_disk_becomes_active_is_then_started()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.NodeDevices.WakeDoesNotTake = true;
        harness.Wake();
        harness.Tick(2);
        Assert.That(harness.Log.CountOf("start 0"), Is.EqualTo(0));

        harness.NodeDevices.WakeDoesNotTake = false;
        harness.Tick(2);

        Assert.That(harness.Log.CountOf("start 0"), Is.EqualTo(1));
        Assert.That(harness.State, Is.EqualTo(StoragePlaneState.Ready));
    }

    /// <summary>
    /// A node that reports OSDs but no disks — a container runtime, or the E2E node — must still
    /// be able to reach READY.
    /// </summary>
    [Test]
    public void An_osd_with_no_known_disk_is_started_anyway()
    {
        var harness = new PlaneHarness().WithHost();
        harness.Osds.Seed(new OsdDto
        {
            OsdId = 3,
            HostId = "h1",
            DeviceId = "missing",
            Up = false,
            In = true,
            ProcessRunning = false
        });
        harness.Wake();

        harness.Tick(2);

        Assert.That(harness.Log.CountOf("start 3"), Is.EqualTo(1));
    }

    // ---- fail closed --------------------------------------------------------

    /// <summary>
    /// When a node cannot be reached, Control must keep believing the OSD is stopped. Recording
    /// the requested state instead let an unreachable node take the plane to READY.
    /// </summary>
    [Test]
    public void An_unreachable_node_leaves_the_osd_recorded_as_stopped()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.NodeOsds.ThrowOnStart = true;
        harness.Wake();

        harness.Tick(3);

        Assert.That(harness.Osds.IsEveryProcessRunning(), Is.False);
        Assert.That(harness.State, Is.EqualTo(StoragePlaneState.Waking));
    }

    [Test]
    public void One_host_failing_still_starts_the_others()
    {
        var harness = TwoHosts();
        harness.NodeOsds.ThrowOnStartFor.Add(new Uri("http://127.0.0.1:7081"));
        harness.Wake();

        harness.Tick(2);

        Assert.That(harness.Log.Commands, Does.Contain("7082 start 1"));
        Assert.That(harness.Log.Commands, Does.Not.Contain("7081 start 0"));
    }

    [Test]
    public void A_node_failure_does_not_escape_the_tick()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.NodeOsds.ThrowOnStart = true;
        harness.Wake();

        Assert.DoesNotThrow(() => harness.Tick(2));
    }

    [Test]
    public void A_node_failure_does_not_prevent_a_later_start()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();
        harness.NodeOsds.ThrowOnStart = true;
        harness.Wake();
        harness.Tick(2);

        harness.NodeOsds.ThrowOnStart = false;
        harness.Tick(2);

        Assert.That(harness.Log.Commands, Does.Contain("7080 start 0"));
        Assert.That(harness.State, Is.EqualTo(StoragePlaneState.Ready));
    }

    // ---- inventory ----------------------------------------------------------

    [Test]
    public void An_observation_from_one_host_does_not_drop_the_other()
    {
        var harness = TwoHosts();

        Assert.That(harness.Osds.GetOsd(0)?.HostId, Is.EqualTo("node-a"));
        Assert.That(harness.Osds.GetOsd(1)?.HostId, Is.EqualTo("node-b"));
    }

    /// <summary>
    /// A node that cannot enumerate reports an error and an empty list. Erasing inventory on that
    /// made "every process is stopped" vacuously true, so the plane could conclude it was asleep.
    /// </summary>
    [Test]
    public void An_error_observation_with_no_osds_does_not_erase_inventory()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();

        harness.Osds.ApplyObserved(new HostOsdsObservationDto
        {
            HostId = "h1",
            Osds = [],
            Error = "systemctl: command not found"
        });

        Assert.That(harness.Osds.ListOsds(), Has.Count.EqualTo(1));
        Assert.That(harness.Osds.ListObservationErrors().Single(), Does.Contain("systemctl"));
    }

    [Test]
    public void A_clean_observation_with_no_osds_does_empty_the_host()
    {
        var harness = new PlaneHarness().WithHost().WithColdOsd();

        harness.Osds.ApplyObserved(new HostOsdsObservationDto
        {
            HostId = "h1",
            Osds = [],
            Error = null
        });

        Assert.That(harness.Osds.ListOsds(), Is.Empty);
        Assert.That(harness.Osds.ListObservationErrors(), Is.Empty);
    }

    private static PlaneHarness TwoHosts()
    {
        var harness = new PlaneHarness().WithHost("node-a", 7081).WithHost("node-b", 7082);
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

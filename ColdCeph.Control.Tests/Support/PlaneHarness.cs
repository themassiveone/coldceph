using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Devices.Controllers;
using ColdCeph.Control.Features.Devices.Services;
using ColdCeph.Control.Features.Hosts.Controllers;
using ColdCeph.Control.Features.Hosts.Services;
using ColdCeph.Control.Features.Integrity.Controllers;
using ColdCeph.Control.Features.Integrity.Services;
using ColdCeph.Control.Features.Osds.Controllers;
using ColdCeph.Control.Features.Osds.Services;
using ColdCeph.Control.Features.S3.Controllers;
using ColdCeph.Control.Features.S3.Repositories;
using ColdCeph.Control.Features.S3.Services;
using ColdCeph.Control.Features.StoragePlane.Controllers;
using ColdCeph.Control.Features.StoragePlane.Services;
using ColdCeph.Control.Tests.Fake;
using ColdCeph.Core.Features.Devices.DTOs;
using ColdCeph.Core.Features.Hosts.DTOs;
using ColdCeph.Core.Features.Operations.DTOs;
using ColdCeph.Core.Features.Osds.DTOs;
using ColdCeph.Core.Features.StoragePlane.DTOs;
using Microsoft.Extensions.Logging.Abstractions;

namespace ColdCeph.Control.Tests.Support;

/// <summary>
/// All three reconcile loops over one set of services, so a test can drive several ticks and
/// assert on the <em>sequence</em> — how many commands a wake issues, in what order across
/// loops, and whether a transition takes more than one tick.
/// <para>
/// A single <see cref="ReconcileOnce"/> per test could not see any of that, which is how a
/// per-second command storm and a missing disk-before-OSD ordering both went unnoticed.
/// </para>
/// </summary>
public sealed class PlaneHarness
{
    public FakeClock Clock { get; } = new();
    public ControlConfig Config { get; }
    public NodeCommandLog Log { get; } = new();
    public StoragePlaneService Plane { get; }
    public FakeCephQueryProvider Ceph { get; } = new();
    public IntegrityService Integrity { get; }
    public IntegrityController IntegrityController { get; }
    public MemoryRequestLedger Ledger { get; }
    public OsdsService Osds { get; }
    public DevicesService Devices { get; }
    public HostsService Hosts { get; }
    public FakeNodeOsdsClient NodeOsds { get; }
    public FakeNodeDevicesClient NodeDevices { get; }
    public StoragePlaneReconciler PlaneLoop { get; }
    public OsdsReconciler OsdsLoop { get; }
    public DevicesReconciler DevicesLoop { get; }

    public PlaneHarness(TimeSpan? idleTimeout = null)
    {
        Config = new ControlConfig
        {
            BindHttpListeners = false,
            IdleTimeout = idleTimeout ?? TimeSpan.FromMinutes(15)
        };
        Plane = new StoragePlaneService(
            new MemoryStoragePlaneRepository(), new RecordingNooutProvider(), Clock, Config);
        var planeController = new StoragePlaneController(Plane);
        Integrity = new IntegrityService(Ceph, new MemoryIntegrityRepository(), planeController, Clock);
        IntegrityController = new IntegrityController(Integrity);
        Ledger = new MemoryRequestLedger(Clock);
        var s3 = new S3Controller(new S3Service(Ledger, planeController, IntegrityController, new FakeRgwProxy(), Config));

        NodeOsds = new FakeNodeOsdsClient(Log);
        NodeDevices = new FakeNodeDevicesClient(Log);
        Osds = new OsdsService(NodeOsds, Config);
        Devices = new DevicesService(NodeDevices, Config);
        Hosts = new HostsService(Config, Clock);

        var osdsController = new OsdsController(Osds, IntegrityController);
        var devicesController = new DevicesController(Devices);

        PlaneLoop = new StoragePlaneReconciler(
            Plane, s3, IntegrityController, osdsController, devicesController, NullLogger<StoragePlaneReconciler>.Instance);
        OsdsLoop = new OsdsReconciler(
            Osds, planeController, new HostsController(Hosts), devicesController, NullLogger<OsdsReconciler>.Instance);
        DevicesLoop = new DevicesReconciler(
            Devices, planeController, osdsController, new HostsController(Hosts), NullLogger<DevicesReconciler>.Instance);
    }

    public PlaneHarness WithHost(string hostId = "h1", int port = 7080)
    {
        Hosts.RegisterHeartbeat(
            new NodeStatusDto { HostId = hostId, Hostname = hostId, ObservedAt = Clock.UtcNow },
            new Uri($"http://127.0.0.1:{port}"));
        return this;
    }

    /// <summary>One OSD on one disk, both as a cold appliance reports them: stopped and spun down.</summary>
    public PlaneHarness WithColdOsd(string hostId = "h1", int osdId = 0, string deviceId = "d0")
    {
        Osds.Seed(new OsdDto
        {
            OsdId = osdId,
            HostId = hostId,
            DeviceId = deviceId,
            Up = false,
            In = true,
            ProcessRunning = false
        });
        Devices.Seed(new DeviceDto
        {
            DeviceId = deviceId,
            HostId = hostId,
            MappedOsdId = osdId,
            Wwn = $"wwn-{deviceId}",
            Serial = $"serial-{deviceId}",
            Path = $"/dev/{deviceId}",
            PowerState = DevicePowerState.Standby
        });
        return this;
    }

    /// <summary>Runs every loop once, the way the three hosted services each fire on their timer.</summary>
    public PlaneHarness Tick(int times = 1)
    {
        for (var index = 0; index < times; index++)
        {
            PlaneLoop.ReconcileOnce();
            DevicesLoop.ReconcileOnce();
            OsdsLoop.ReconcileOnce();
        }

        return this;
    }

    public StoragePlaneState State => Plane.GetState().State;

    public string NewOperationId() => OperationIdRules.Create().Value;

    public PlaneHarness Wake()
    {
        Plane.MarkObserved(StoragePlaneState.Cold, "startup-reconcile");
        Plane.RequestWake(NewOperationId(), "operator");
        return this;
    }

    /// <summary>Confirms integrity, which is what StoragePlane reads when deciding to sleep.</summary>
    public PlaneHarness Confirm()
    {
        _ = Integrity.GetIntegrity();
        return this;
    }
}

using ColdCeph.Control.Features.StoragePlane.ViewModels;
using ColdCeph.Core.Features.Devices.DTOs;
using ColdCeph.Core.Features.Hosts.DTOs;
using ColdCeph.Core.Features.Integrity.DTOs;
using ColdCeph.Core.Features.Operations.DTOs;
using ColdCeph.Core.Features.Osds.DTOs;
using ColdCeph.Core.Features.S3.DTOs;
using ColdCeph.Core.Features.StoragePlane.DTOs;

namespace ColdCeph.Control.Tests.Features.StoragePlane.Controller;

[TestFixture]
public sealed class StoragePlanePagesControllerTests
{
    [Test]
    public void Overview_does_not_offer_wake_before_storage_is_discovered()
    {
        var model = Model(StoragePlaneState.Cold, configured: false);

        Assert.That(model.SetupComplete, Is.False);
        Assert.That(model.CanWake, Is.False);
        Assert.That(model.Headline, Is.EqualTo("Finish setting up storage"));
    }

    [Test]
    public void Overview_offers_wake_from_cold_after_storage_is_discovered()
    {
        var model = Model(StoragePlaneState.Cold);

        Assert.That(model.SetupComplete, Is.True);
        Assert.That(model.CanWake, Is.True);
        Assert.That(model.CanSleep, Is.False);
    }

    [Test]
    public void Overview_does_not_offer_wake_or_sleep_while_waking()
    {
        var model = Model(StoragePlaneState.Waking);

        Assert.That(model.CanWake, Is.False);
        Assert.That(model.CanSleep, Is.False);
        Assert.That(model.Headline, Is.EqualTo("Getting storage ready"));
    }

    [Test]
    public void Overview_offers_sleep_from_ready_and_not_wake()
    {
        var model = Model(StoragePlaneState.Ready);

        Assert.That(model.CanSleep, Is.True);
        Assert.That(model.CanWake, Is.False);
        Assert.That(model.Headline, Is.EqualTo("Ready for backups"));
    }

    [Test]
    public void Overview_offers_wake_from_faulted_when_protection_is_clear()
    {
        var model = Model(StoragePlaneState.Faulted);

        Assert.That(model.CanWake, Is.True);
        Assert.That(model.CanSleep, Is.False);
        Assert.That(model.Guidance, Does.Contain("Wake storage to resume"));
        Assert.That(model.Guidance, Does.Not.Contain("Check protection for the cause"));
    }

    [Test]
    public void Overview_does_not_offer_wake_from_faulted_when_objects_are_unfound()
    {
        var model = Model(StoragePlaneState.Faulted, durabilityFailure: true);

        Assert.That(model.CanWake, Is.False);
        Assert.That(model.Guidance, Does.Contain("Check protection for the cause"));
        Assert.That(model.Guidance, Does.Not.Contain("Wake storage to resume"));
    }

    [Test]
    public void Overview_formats_capacity_and_protection_for_people()
    {
        var model = Model(StoragePlaneState.Ready);

        Assert.That(model.CapacityTotal, Is.EqualTo("3 GB"));
        Assert.That(model.CapacityAvailable, Is.EqualTo("2 GB"));
        Assert.That(model.CapacityPercent, Is.EqualTo(33));
        Assert.That(model.ProtectionSummary, Does.StartWith("Protected when last checked"));
    }

    [Test]
    public void Overview_labels_absent_capacity_as_not_checked()
    {
        var model = Model(StoragePlaneState.Cold, capacityKnown: false);

        Assert.That(model.CapacityAvailable, Is.EqualTo("Not checked"));
        Assert.That(model.CapacityPercent, Is.Null);
    }

    private static StoragePlanePageViewModel Model(
        StoragePlaneState state,
        bool configured = true,
        bool capacityKnown = true,
        bool durabilityFailure = false)
    {
        ClusterCapacityDto? capacity = capacityKnown ? new ClusterCapacityDto
        {
            TotalBytes = 3_000_000_000,
            UsedBytes = 1_000_000_000,
            AvailableBytes = 2_000_000_000
        } : null;

        var now = new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
        var snapshot = new StoragePlaneSnapshot
        {
            State = state,
            LeaseHolder = null,
            ActiveOperationId = state is StoragePlaneState.Waking or StoragePlaneState.Ready ? OperationIdRules.Create().Value : null,
            JournalStep = null,
            ObservedAt = now,
            Trusted = true
        };
        var integrity = new IntegritySnapshot
        {
            Raw = new CephHealthRaw
            {
                Status = durabilityFailure ? "HEALTH_ERR" : "HEALTH_OK",
                Summary = durabilityFailure ? "unfound objects" : "HEALTH_OK",
                Checks = durabilityFailure ? ["OBJECT_UNFOUND: unfound objects"] : []
            },
            Checks = durabilityFailure
                ?
                [
                    new ClassifiedHealthCheck
                    {
                        Name = "OBJECT_UNFOUND",
                        Detail = "OBJECT_UNFOUND: unfound objects",
                        Classification = HealthClassification.Unexpected,
                        Durability = true
                    }
                ]
                : [],
            DurabilityFailure = durabilityFailure,
            Predicates = new ReadinessPredicates
            {
                ControlPlaneAvailable = !durabilityFailure,
                OsdPlaneExpected = !durabilityFailure,
                ReadReady = !durabilityFailure,
                WriteReady = !durabilityFailure,
                SleepSafe = !durabilityFailure
            },
            Capacity = capacity,
            CapacityUnavailableReason = capacity is null ? "not checked" : null,
            LastVerifiedCleanAt = now,
            LastVerifiedCleanSummary = "HEALTH_OK",
            ObservedAt = now
        };
        var hosts = configured
            ? new[] { new HostDto { HostId = "node-a", Hostname = "node-a", Endpoint = new Uri("http://node-a"), LastHeartbeat = now, Alive = true } }
            : [];
        var devices = configured
            ? new[] { new DeviceDto { DeviceId = "disk-a", HostId = "node-a", MappedOsdId = 0, Wwn = "wwn", Serial = "serial", Path = "/dev/sda", PowerState = DevicePowerState.Active } }
            : [];
        var osds = configured
            ? new[] { new OsdDto { OsdId = 0, HostId = "node-a", DeviceId = "disk-a", Up = true, In = true, ProcessRunning = true } }
            : [];
        return StoragePlanePageViewModel.From(
            snapshot,
            integrity,
            new S3PendingWorkDto { HasPendingWork = false, ActiveCount = 0, QueuedCount = 0, LastActivity = null },
            hosts,
            0,
            devices,
            osds);
    }
}

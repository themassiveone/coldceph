using ColdCeph.Core.Features.StoragePlane.DTOs;
using ColdCeph.Core.Features.Devices.DTOs;
using ColdCeph.Core.Features.Hosts.DTOs;
using ColdCeph.Core.Features.Integrity.DTOs;
using ColdCeph.Core.Features.Osds.DTOs;
using ColdCeph.Core.Features.S3.DTOs;

namespace ColdCeph.Control.Features.StoragePlane.ViewModels;

public sealed class StoragePlanePageViewModel
{
    public required StoragePlaneSnapshot Snapshot { get; init; }
    public required IntegritySnapshot Integrity { get; init; }
    public required S3PendingWorkDto Work { get; init; }
    public required int HostCount { get; init; }
    public required int AliveHostCount { get; init; }
    public required int PendingHostCount { get; init; }
    public required int DiskCount { get; init; }
    public required int StandbyDiskCount { get; init; }
    public required int StorageServiceCount { get; init; }
    public required int RunningStorageServiceCount { get; init; }

    public bool SetupComplete => HostCount > 0
                                 && AliveHostCount == HostCount
                                 && PendingHostCount == 0
                                 && DiskCount > 0
                                 && StorageServiceCount > 0;

    public bool CanWake => Snapshot.State == StoragePlaneState.Cold && SetupComplete;

    public bool CanSleep => Snapshot.State == StoragePlaneState.Ready;

    public string Headline => Snapshot.State switch
    {
        _ when !SetupComplete => "Finish setting up storage",
        StoragePlaneState.Cold => "Storage is asleep",
        StoragePlaneState.Waking => "Getting storage ready",
        StoragePlaneState.Ready => Integrity.Predicates.WriteReady ? "Ready for backups" : "Storage needs attention",
        StoragePlaneState.Quiescing => "Preparing to sleep",
        StoragePlaneState.Sleeping => "Going to sleep",
        StoragePlaneState.Faulted => "Storage needs attention",
        _ => Snapshot.State.ToString()
    };

    public string Guidance => Snapshot.State switch
    {
        _ when !SetupComplete =>
            "Connect each storage machine and confirm that its disks and storage services appear.",
        StoragePlaneState.Cold =>
            ProtectionSummary,
        StoragePlaneState.Waking =>
            "Disks and storage services are starting. Backup requests will continue when storage is ready.",
        StoragePlaneState.Ready =>
            Integrity.Predicates.WriteReady
                ? "Backup clients can read and write now."
                : "New writes are blocked. Review Data protection before continuing.",
        StoragePlaneState.Quiescing =>
            "Current activity is finishing before disks are parked.",
        StoragePlaneState.Sleeping =>
            "Storage services are stopping and disks are entering standby.",
        StoragePlaneState.Faulted =>
            "Automatic transitions have stopped. Review Data protection for the cause and next action.",
        _ => string.Empty
    };

    public string Tone => Snapshot.State switch
    {
        _ when !SetupComplete => "busy",
        StoragePlaneState.Ready => "ok",
        StoragePlaneState.Waking or StoragePlaneState.Quiescing or StoragePlaneState.Sleeping => "busy",
        StoragePlaneState.Faulted => "bad",
        _ => "idle"
    };

    public string AvailabilitySummary => Snapshot.State switch
    {
        StoragePlaneState.Ready when Integrity.Predicates.WriteReady => "Reads and writes available",
        StoragePlaneState.Ready => "Reads may be available; writes blocked",
        StoragePlaneState.Cold => "Asleep",
        StoragePlaneState.Waking => "Starting",
        StoragePlaneState.Quiescing or StoragePlaneState.Sleeping => "Stopping",
        StoragePlaneState.Faulted => "Unavailable",
        _ => "Unknown"
    };

    public string ProtectionSummary => Integrity.LastVerifiedCleanAt is { } verified
        ? $"Protected when last checked {verified:u}"
        : "Protection has not been verified yet";

    public string CapacityUsed => FormatBytes(Integrity.Capacity?.UsedBytes);
    public string CapacityAvailable => FormatBytes(Integrity.Capacity?.AvailableBytes);
    public string CapacityTotal => FormatBytes(Integrity.Capacity?.TotalBytes);
    public int? CapacityPercent => Integrity.Capacity is { TotalBytes: > 0 } capacity
        ? (int)Math.Round(capacity.UsedBytes * 100d / capacity.TotalBytes)
        : null;

    public static StoragePlanePageViewModel From(
        StoragePlaneSnapshot snapshot,
        IntegritySnapshot integrity,
        S3PendingWorkDto work,
        IReadOnlyList<HostDto> hosts,
        int pendingHosts,
        IReadOnlyList<DeviceDto> devices,
        IReadOnlyList<OsdDto> osds)
        => new()
        {
            Snapshot = snapshot,
            Integrity = integrity,
            Work = work,
            HostCount = hosts.Count,
            AliveHostCount = hosts.Count(host => host.Alive),
            PendingHostCount = pendingHosts,
            DiskCount = devices.Count,
            StandbyDiskCount = devices.Count(device => device.PowerState == DevicePowerState.Standby),
            StorageServiceCount = osds.Count,
            RunningStorageServiceCount = osds.Count(osd => osd.ProcessRunning)
        };

    private static string FormatBytes(long? bytes)
    {
        if (bytes is null)
            return "Not checked";
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        var value = (double)bytes.Value;
        var unit = 0;
        while (value >= 1000 && unit < units.Length - 1)
        {
            value /= 1000;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }
}

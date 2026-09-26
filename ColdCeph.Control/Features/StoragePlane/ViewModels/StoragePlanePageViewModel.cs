using ColdCeph.Core.Features.StoragePlane.DTOs;

namespace ColdCeph.Control.Features.StoragePlane.ViewModels;

public sealed class StoragePlanePageViewModel
{
    public required StoragePlaneSnapshot Snapshot { get; init; }

    public bool CanWake => Snapshot.State == StoragePlaneState.Cold;

    public bool CanSleep => Snapshot.State == StoragePlaneState.Ready;

    public string Headline => Snapshot.State switch
    {
        StoragePlaneState.Cold => "Disks are parked",
        StoragePlaneState.Waking => "Waking the data plane",
        StoragePlaneState.Ready => "Data plane is live",
        StoragePlaneState.Quiescing => "Preparing to sleep",
        StoragePlaneState.Sleeping => "Parking disks",
        StoragePlaneState.Faulted => "Storage plane is faulted",
        _ => Snapshot.State.ToString()
    };

    public string Guidance => Snapshot.State switch
    {
        StoragePlaneState.Cold =>
            "S3 clients wait until you wake the plane. Reload this page when you want a new reading.",
        StoragePlaneState.Waking =>
            "Disks are spinning up and OSDs are starting. Reload this page when you want a new reading.",
        StoragePlaneState.Ready =>
            "S3 is forwarding to RGW. Sleep stops OSD processes, then parks the disks.",
        StoragePlaneState.Quiescing =>
            "OSD processes are stopping so disks can sleep. Reload this page when you want a new reading.",
        StoragePlaneState.Sleeping =>
            "Disks are entering standby. Reload this page when you want a new reading.",
        StoragePlaneState.Faulted =>
            "Do not wake or sleep from here. Open Integrity and read Ceph's own health first.",
        _ => string.Empty
    };

    public string Tone => Snapshot.State switch
    {
        StoragePlaneState.Ready => "ok",
        StoragePlaneState.Waking or StoragePlaneState.Quiescing or StoragePlaneState.Sleeping => "busy",
        StoragePlaneState.Faulted => "bad",
        _ => "idle"
    };

    public static StoragePlanePageViewModel From(StoragePlaneSnapshot snapshot)
        => new() { Snapshot = snapshot };
}

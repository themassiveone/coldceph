using ColdCeph.Core.Features.StoragePlane.DTOs;

namespace ColdCeph.Control.Features.StoragePlane.Models;

public sealed class StoragePlaneRecord
{
    public StoragePlaneState State { get; set; } = StoragePlaneState.Cold;
    public string? LeaseId { get; set; }
    public string? LeaseHolder { get; set; }
    public string? ActiveOperationId { get; set; }
    public DateTimeOffset? LeaseDeadline { get; set; }
    public string JournalStep { get; set; } = "untrusted-startup";
    public DateTimeOffset? LastReadyAt { get; set; }
    public DateTimeOffset? LastTransitionAt { get; set; }
    public bool RealityReconciled { get; set; }
    public List<NooutRecordDto> OwnedNoout { get; set; } = [];
}

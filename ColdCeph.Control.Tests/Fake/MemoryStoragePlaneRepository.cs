using ColdCeph.Control.Features.StoragePlane.Interfaces;
using ColdCeph.Control.Features.StoragePlane.Models;

namespace ColdCeph.Control.Tests.Fake;

public sealed class MemoryStoragePlaneRepository : IStoragePlaneRepository
{
    private StoragePlaneRecord _record = new();

    public StoragePlaneRecord Load()
        => new()
        {
            State = _record.State,
            LeaseId = _record.LeaseId,
            LeaseHolder = _record.LeaseHolder,
            ActiveOperationId = _record.ActiveOperationId,
            LeaseDeadline = _record.LeaseDeadline,
            JournalStep = _record.JournalStep,
            LastReadyAt = _record.LastReadyAt,
            LastTransitionAt = _record.LastTransitionAt,
            RealityReconciled = _record.RealityReconciled,
            OwnedNoout = [.. _record.OwnedNoout]
        };

    public void Save(StoragePlaneRecord record)
    {
        _record = new StoragePlaneRecord
        {
            State = record.State,
            LeaseId = record.LeaseId,
            LeaseHolder = record.LeaseHolder,
            ActiveOperationId = record.ActiveOperationId,
            LeaseDeadline = record.LeaseDeadline,
            JournalStep = record.JournalStep,
            LastReadyAt = record.LastReadyAt,
            LastTransitionAt = record.LastTransitionAt,
            RealityReconciled = record.RealityReconciled,
            OwnedNoout = [.. record.OwnedNoout]
        };
    }
}

using ColdCeph.Core.Features.S3.DTOs;

namespace ColdCeph.Control.Features.S3.Interfaces;

public interface IRequestLedger
{
    S3PendingWorkDto Snapshot();
    Guid BeginQueued();
    void Activate(Guid id);
    void Complete(Guid id);
}

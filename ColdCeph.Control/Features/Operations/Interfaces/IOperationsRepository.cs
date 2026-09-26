using ColdCeph.Core.Features.Operations.DTOs;

namespace ColdCeph.Control.Features.Operations.Interfaces;

public interface IOperationsRepository
{
    void Append(OperationRecordDto operation);
    void AppendEvent(AuditEventDto auditEvent);
    IReadOnlyList<OperationRecordDto> ListOperations();
    IReadOnlyList<AuditEventDto> ListEvents();
}

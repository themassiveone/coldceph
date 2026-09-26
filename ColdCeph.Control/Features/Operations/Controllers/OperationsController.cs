using ColdCeph.Control.Features.Operations.Services;
using ColdCeph.Core.Features.Operations.DTOs;

namespace ColdCeph.Control.Features.Operations.Controllers;

public sealed class OperationsController
{
    private readonly OperationsService _service;

    public OperationsController(OperationsService service)
    {
        _service = service;
    }

    public IReadOnlyList<OperationRecordDto> ListOperations() => _service.ListOperations();

    public IReadOnlyList<AuditEventDto> ListEvents() => _service.ListEvents();

    public void AppendAudit(string operationId, string kind, string initiator, string detail)
        => _service.AppendAudit(operationId, kind, initiator, detail);
}

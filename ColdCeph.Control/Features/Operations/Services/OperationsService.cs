using ColdCeph.Control.Features.Operations.Interfaces;
using ColdCeph.Control.Shared;
using ColdCeph.Core.Features.Operations.DTOs;

namespace ColdCeph.Control.Features.Operations.Services;

public sealed class OperationsService
{
    private readonly IOperationsRepository _repository;
    private readonly IClock _clock;

    public OperationsService(IOperationsRepository repository, IClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public IReadOnlyList<OperationRecordDto> ListOperations() => _repository.ListOperations();

    public IReadOnlyList<AuditEventDto> ListEvents() => _repository.ListEvents();

    public void AppendAudit(string operationId, string kind, string initiator, string detail)
    {
        _repository.Append(new OperationRecordDto
        {
            OperationId = operationId,
            Kind = kind,
            Initiator = initiator,
            Summary = detail,
            StartedAt = _clock.UtcNow,
            CompletedAt = _clock.UtcNow
        });
        _repository.AppendEvent(new AuditEventDto
        {
            EventId = Guid.NewGuid().ToString("N"),
            OperationId = operationId,
            Kind = kind,
            Detail = detail,
            At = _clock.UtcNow
        });
    }
}

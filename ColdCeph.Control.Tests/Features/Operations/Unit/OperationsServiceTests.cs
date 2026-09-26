using ColdCeph.Control.Features.Operations.Services;
using ColdCeph.Control.Tests.Fake;
using ColdCeph.Control.Features.Operations.Interfaces;
using ColdCeph.Core.Features.Operations.DTOs;

namespace ColdCeph.Control.Tests.Features.Operations.Unit;

[TestFixture]
public sealed class OperationsServiceTests
{
    [Test]
    public void AppendAudit_is_visible_on_list()
    {
        var service = new OperationsService(new MemoryOperationsRepository(), new FakeClock());
        var id = OperationIdRules.Create().Value;

        service.AppendAudit(id, "wake", "operator", "wake requested");

        Assert.That(service.ListOperations().Single().OperationId, Is.EqualTo(id));
        Assert.That(service.ListEvents(), Has.Count.EqualTo(1));
    }

    [Test]
    public void List_is_empty_when_nothing_was_appended()
    {
        var service = new OperationsService(new MemoryOperationsRepository(), new FakeClock());

        Assert.That(service.ListOperations(), Is.Empty);
        Assert.That(service.ListEvents(), Is.Empty);
    }
}

public sealed class MemoryOperationsRepository : IOperationsRepository
{
    private readonly List<OperationRecordDto> _operations = [];
    private readonly List<AuditEventDto> _events = [];

    public void Append(OperationRecordDto operation) => _operations.Add(operation);
    public void AppendEvent(AuditEventDto auditEvent) => _events.Add(auditEvent);
    public IReadOnlyList<OperationRecordDto> ListOperations() => _operations.ToArray();
    public IReadOnlyList<AuditEventDto> ListEvents() => _events.ToArray();
}

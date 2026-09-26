using ColdCeph.Control.Features.StoragePlane.Services;
using ColdCeph.Core.Features.Operations.DTOs;
using ColdCeph.Core.Features.StoragePlane.DTOs;

namespace ColdCeph.Control.Features.StoragePlane.Controllers;

public sealed class StoragePlaneController
{
    private readonly StoragePlaneService _service;

    public StoragePlaneController(StoragePlaneService service)
    {
        _service = service;
    }

    public StoragePlaneSnapshot GetState() => _service.GetState();

    public TransitionLeaseDto? GetLease() => _service.GetLease();

    public IdlePolicyDto GetIdlePolicy() => _service.GetIdlePolicy();

    public IReadOnlyList<NooutRecordDto> ListOwnedNoout() => _service.ListOwnedNoout();

    public StoragePlaneReadinessDto GetReadiness(bool readReady, bool writeReady)
        => _service.GetReadiness(readReady, writeReady);

    public bool IsIdle(DateTimeOffset? lastActivity) => _service.IsIdle(lastActivity);

    public TransitionLeaseDto RequestWake(string operationId, string initiator)
        => _service.RequestWake(operationId, initiator);

    public void RequestSleep(string operationId, string initiator)
        => _service.RequestSleep(operationId, initiator);
}

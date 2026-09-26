using ColdCeph.Control.Features.Integrity.Services;
using ColdCeph.Core.Features.Integrity.DTOs;

namespace ColdCeph.Control.Features.Integrity.Controllers;

public sealed class IntegrityController
{
    private readonly IntegrityService _service;

    public IntegrityController(IntegrityService service)
    {
        _service = service;
    }

    public IntegritySnapshot GetIntegrity() => _service.GetIntegrity();

    public ReadinessPredicates GetPredicates() => _service.GetIntegrity().Predicates;
}

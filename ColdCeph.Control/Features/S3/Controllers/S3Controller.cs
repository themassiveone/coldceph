using ColdCeph.Control.Features.S3.Services;
using ColdCeph.Core.Features.S3.DTOs;

namespace ColdCeph.Control.Features.S3.Controllers;

public sealed class S3Controller
{
    private readonly S3Service _service;

    public S3Controller(S3Service service)
    {
        _service = service;
    }

    public S3PendingWorkDto GetPendingWork() => _service.GetPendingWork();

    public bool HasPendingWork() => _service.HasPendingWork();
}

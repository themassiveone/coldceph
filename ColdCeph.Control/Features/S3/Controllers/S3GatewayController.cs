using ColdCeph.Control.Features.S3.Services;

namespace ColdCeph.Control.Features.S3.Controllers;

public sealed class S3GatewayController
{
    private readonly S3Service _service;

    public S3GatewayController(S3Service service)
    {
        _service = service;
    }

    public Task HandleAsync(HttpContext context) => _service.HandleAsync(context);
}

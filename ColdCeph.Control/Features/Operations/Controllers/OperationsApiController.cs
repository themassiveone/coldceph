using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdCeph.Control.Features.Operations.Controllers;

[Authorize]
[Route("v1")]
public sealed class OperationsApiController : ControllerBase
{
    private readonly OperationsController _operations;

    public OperationsApiController(OperationsController operations)
    {
        _operations = operations;
    }

    [HttpGet("operations")]
    public IActionResult GetOperations() => Ok(_operations.ListOperations());

    [HttpGet("events")]
    public IActionResult GetEvents() => Ok(_operations.ListEvents());
}

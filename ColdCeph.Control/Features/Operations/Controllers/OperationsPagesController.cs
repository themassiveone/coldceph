using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdCeph.Control.Features.Operations.Controllers;

[Authorize]
public sealed class OperationsPagesController : Controller
{
    private readonly OperationsController _operations;

    public OperationsPagesController(OperationsController operations)
    {
        _operations = operations;
    }

    [HttpGet("/operations")]
    public IActionResult Index() => View("Index", _operations.ListOperations());
}

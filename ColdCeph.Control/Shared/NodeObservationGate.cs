using ColdCeph.Control.Features.Hosts.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ColdCeph.Control.Shared;

public static class NodeObservationGate
{
    public static IActionResult? Reject(HttpRequest request, string expectedToken, HostsController hosts, string hostId)
    {
        if (!request.Headers.TryGetValue("X-ColdCeph-Token", out var token) || token != expectedToken)
            return new UnauthorizedResult();
        if (string.IsNullOrWhiteSpace(hostId) || hosts.GetHost(hostId) is null)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);
        return null;
    }
}

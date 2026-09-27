using ColdCeph.Control.Features.Osds.Interfaces;
using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Control.Tests.Fake;

public sealed class FakeNodeOsdsClient : INodeOsdsClient
{
    public List<string> Commands { get; } = [];
    public bool Running { get; set; }

    public bool ThrowOnStart { get; set; }

    public HashSet<Uri> ThrowOnStartFor { get; } = [];

    public OsdMutationResult Start(Uri endpoint, OsdMutationRequest request)
    {
        if (ThrowOnStart || ThrowOnStartFor.Contains(endpoint))
            throw new InvalidOperationException("node down");
        Commands.Add($"{endpoint.Port} start {request.OsdId}");
        Running = true;
        return new OsdMutationResult(request.OsdId, true, false);
    }

    public OsdMutationResult Stop(Uri endpoint, OsdMutationRequest request)
    {
        Commands.Add($"{endpoint.Port} stop {request.OsdId}");
        Running = false;
        return new OsdMutationResult(request.OsdId, false, false);
    }
}

using ColdCeph.Control.Features.Osds.Interfaces;
using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Control.Tests.Fake;

public sealed class FakeNodeOsdsClient : INodeOsdsClient
{
    public List<string> Commands { get; } = [];
    public bool Running { get; set; }

    public List<OsdDto> Inventory { get; set; } = [];

    public Dictionary<Uri, List<OsdDto>> InventoryByEndpoint { get; } = [];

    public bool ThrowOnList { get; set; }

    public HashSet<Uri> ThrowOnListFor { get; } = [];

    public IReadOnlyList<OsdDto> List(Uri endpoint)
    {
        if (ThrowOnList || ThrowOnListFor.Contains(endpoint))
            throw new InvalidOperationException("node down");
        if (InventoryByEndpoint.TryGetValue(endpoint, out var listed))
            return listed;
        return Inventory;
    }

    public OsdMutationResult Start(Uri endpoint, OsdMutationRequest request)
    {
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

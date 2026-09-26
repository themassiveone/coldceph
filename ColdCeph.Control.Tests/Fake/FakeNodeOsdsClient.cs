using ColdCeph.Control.Features.Osds.Interfaces;
using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Control.Tests.Fake;

public sealed class FakeNodeOsdsClient : INodeOsdsClient
{
    public List<string> Commands { get; } = [];
    public bool Running { get; set; }

    public List<OsdDto> Inventory { get; set; } = [];

    public bool ThrowOnList { get; set; }

    public IReadOnlyList<OsdDto> List(Uri endpoint)
    {
        if (ThrowOnList)
            throw new InvalidOperationException("node down");
        return Inventory;
    }

    public OsdMutationResult Start(Uri endpoint, OsdMutationRequest request)
    {
        Commands.Add($"start {request.OsdId}");
        Running = true;
        return new OsdMutationResult(request.OsdId, true, false);
    }

    public OsdMutationResult Stop(Uri endpoint, OsdMutationRequest request)
    {
        Commands.Add($"stop {request.OsdId}");
        Running = false;
        return new OsdMutationResult(request.OsdId, false, false);
    }
}

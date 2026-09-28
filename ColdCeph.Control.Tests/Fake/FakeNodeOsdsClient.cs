using ColdCeph.Control.Features.Osds.Interfaces;
using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Control.Tests.Fake;

/// <summary>
/// Stands in for a node's OSD endpoint.
/// <para>
/// It can do the three things a real node does: succeed, fail to answer, and — the case the old
/// fake could not express — answer honestly that the OSD is still not running. That third case is
/// what happens for the first tens of seconds of every real wake, and while it went untested
/// Control's HTTP client was free to invent success.
/// </para>
/// </summary>
public sealed class FakeNodeOsdsClient : INodeOsdsClient
{
    private readonly NodeCommandLog _log;

    public FakeNodeOsdsClient(NodeCommandLog? log = null)
    {
        _log = log ?? new NodeCommandLog();
    }

    public IReadOnlyList<string> Commands => _log.Commands;

    public bool ThrowOnStart { get; set; }

    public HashSet<Uri> ThrowOnStartFor { get; } = [];

    /// <summary>The node accepts the command but the process is not running yet.</summary>
    public bool StartDoesNotTake { get; set; }

    /// <summary>The node accepts the stop but the process is still running.</summary>
    public bool StopDoesNotTake { get; set; }

    public OsdMutationResult Start(Uri endpoint, OsdMutationRequest request)
    {
        if (ThrowOnStart || ThrowOnStartFor.Contains(endpoint))
            throw new InvalidOperationException("node down");
        _log.Add($"{endpoint.Port} start {request.OsdId}");
        return new OsdMutationResult(request.OsdId, !StartDoesNotTake, false);
    }

    public OsdMutationResult Stop(Uri endpoint, OsdMutationRequest request)
    {
        _log.Add($"{endpoint.Port} stop {request.OsdId}");
        return new OsdMutationResult(request.OsdId, StopDoesNotTake, false);
    }
}

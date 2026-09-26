using ColdCeph.Core.Features.Hosts.DTOs;

namespace ColdCeph.Node.Features.Hosts.Interfaces;

public interface IControlHeartbeatClient
{
    void Send(NodeStatusDto status, Uri advertiseEndpoint);
}

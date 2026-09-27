using ColdCeph.Core.Features.Hosts.DTOs;

namespace ColdCeph.Node.Features.Hosts.Interfaces;

public interface IControlHeartbeatClient
{
    int Send(NodeStatusDto status, Uri advertiseEndpoint);
}

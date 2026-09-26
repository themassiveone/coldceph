using ColdCeph.Core.Features.Hosts.DTOs;

namespace ColdCeph.Agent.Features.Hosts.Interfaces;

public interface IControlHeartbeatClient
{
    void Send(AgentStatusDto status, Uri advertiseEndpoint);
}

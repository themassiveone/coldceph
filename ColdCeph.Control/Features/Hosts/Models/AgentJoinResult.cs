using ColdCeph.Core.Features.Hosts.DTOs;

namespace ColdCeph.Control.Features.Hosts.Models;

public sealed record AgentJoinResult(int StatusCode, HostDto? Host);

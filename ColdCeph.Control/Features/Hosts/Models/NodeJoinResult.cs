using ColdCeph.Core.Features.Hosts.DTOs;

namespace ColdCeph.Control.Features.Hosts.Models;

public sealed record NodeJoinResult(int StatusCode, HostDto? Host);

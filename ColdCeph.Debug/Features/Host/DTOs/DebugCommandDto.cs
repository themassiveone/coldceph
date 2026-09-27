namespace ColdCeph.Debug.Features.Host.DTOs;

public sealed record DebugCommandDto(string Verb, string? PagePath, bool RebuildImages = false, string? HostId = null);

namespace ColdCeph.Core.Features.Osds.DTOs;

public sealed record OsdMutationResult(int OsdId, bool ProcessRunning, bool IdempotentHit);

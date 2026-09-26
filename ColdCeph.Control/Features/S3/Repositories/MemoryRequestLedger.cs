using ColdCeph.Control.Features.S3.Interfaces;
using ColdCeph.Control.Shared;
using ColdCeph.Core.Features.S3.DTOs;

namespace ColdCeph.Control.Features.S3.Repositories;

public sealed class MemoryRequestLedger : IRequestLedger
{
    private readonly IClock _clock;
    private readonly object _gate = new();
    private readonly HashSet<Guid> _queued = [];
    private readonly HashSet<Guid> _active = [];
    private DateTimeOffset? _lastActivity;

    public MemoryRequestLedger(IClock clock)
    {
        _clock = clock;
    }

    public S3PendingWorkDto Snapshot()
    {
        lock (_gate)
        {
            return new S3PendingWorkDto
            {
                HasPendingWork = _queued.Count > 0 || _active.Count > 0,
                ActiveCount = _active.Count,
                QueuedCount = _queued.Count,
                LastActivity = _lastActivity
            };
        }
    }

    public Guid BeginQueued()
    {
        lock (_gate)
        {
            var id = Guid.NewGuid();
            _queued.Add(id);
            _lastActivity = _clock.UtcNow;
            return id;
        }
    }

    public void Activate(Guid id)
    {
        lock (_gate)
        {
            _queued.Remove(id);
            _active.Add(id);
            _lastActivity = _clock.UtcNow;
        }
    }

    public void Complete(Guid id)
    {
        lock (_gate)
        {
            _queued.Remove(id);
            _active.Remove(id);
            _lastActivity = _clock.UtcNow;
        }
    }
}

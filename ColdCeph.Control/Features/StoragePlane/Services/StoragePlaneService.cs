using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.StoragePlane.Interfaces;
using ColdCeph.Control.Features.StoragePlane.Models;
using ColdCeph.Control.Shared;
using ColdCeph.Core.Features.Operations.DTOs;
using ColdCeph.Core.Features.StoragePlane.DTOs;

namespace ColdCeph.Control.Features.StoragePlane.Services;

public sealed class StoragePlaneService
{
    private static readonly HashSet<(StoragePlaneState From, StoragePlaneState To)> Allowed =
    [
        (StoragePlaneState.Cold, StoragePlaneState.Waking),
        (StoragePlaneState.Cold, StoragePlaneState.Faulted),
        (StoragePlaneState.Waking, StoragePlaneState.Ready),
        (StoragePlaneState.Waking, StoragePlaneState.Faulted),
        (StoragePlaneState.Ready, StoragePlaneState.Quiescing),
        (StoragePlaneState.Ready, StoragePlaneState.Faulted),
        (StoragePlaneState.Quiescing, StoragePlaneState.Sleeping),
        (StoragePlaneState.Quiescing, StoragePlaneState.Waking),
        (StoragePlaneState.Quiescing, StoragePlaneState.Faulted),
        (StoragePlaneState.Sleeping, StoragePlaneState.Cold),
        (StoragePlaneState.Sleeping, StoragePlaneState.Faulted),
        (StoragePlaneState.Faulted, StoragePlaneState.Waking)
    ];

    private readonly IStoragePlaneRepository _repository;
    private readonly INooutProvider _noout;
    private readonly IClock _clock;
    private readonly ControlConfig _config;
    private readonly object _gate = new();

    public StoragePlaneService(
        IStoragePlaneRepository repository,
        INooutProvider noout,
        IClock clock,
        ControlConfig config)
    {
        _repository = repository;
        _noout = noout;
        _clock = clock;
        _config = config;
    }

    public StoragePlaneSnapshot GetState()
    {
        var record = _repository.Load();
        return ToSnapshot(record);
    }

    public TransitionLeaseDto? GetLease()
    {
        var record = _repository.Load();
        if (record.LeaseId is null || record.LeaseHolder is null || record.ActiveOperationId is null || record.LeaseDeadline is null)
            return null;

        return new TransitionLeaseDto
        {
            LeaseId = record.LeaseId,
            Holder = record.LeaseHolder,
            OperationId = record.ActiveOperationId,
            AcquiredAt = record.LastTransitionAt ?? _clock.UtcNow,
            Deadline = record.LeaseDeadline.Value
        };
    }

    public IdlePolicyDto GetIdlePolicy()
        => new(_config.IdleTimeout, SleepEnabled: true);

    public IReadOnlyList<NooutRecordDto> ListOwnedNoout()
        => _repository.Load().OwnedNoout.ToArray();

    public bool IsIdle(DateTimeOffset? lastActivity)
    {
        var record = _repository.Load();
        var anchor = lastActivity ?? record.LastReadyAt ?? record.LastTransitionAt;
        if (anchor is null)
            return false;
        return _clock.UtcNow - anchor >= _config.IdleTimeout;
    }

    public StoragePlaneReadinessDto GetReadiness(bool readReady, bool writeReady)
    {
        var state = _repository.Load().State;
        var forwarding = state == StoragePlaneState.Ready;
        return new StoragePlaneReadinessDto(forwarding && readReady, forwarding && writeReady);
    }

    public TransitionLeaseDto RequestWake(string operationId, string initiator)
    {
        EnsureOperationId(operationId);
        lock (_gate)
        {
            var record = _repository.Load();
            if (record.State is StoragePlaneState.Waking or StoragePlaneState.Ready)
                return ExistingOrSynthetic(record, operationId, initiator);

            Transition(record, StoragePlaneState.Waking, "wake-requested", operationId, initiator, requireLease: true);
            return GetLease()!;
        }
    }

    public void RequestSleep(string operationId, string initiator)
    {
        EnsureOperationId(operationId);
        lock (_gate)
        {
            var record = _repository.Load();
            if (record.State != StoragePlaneState.Ready)
                throw new InvalidOperationException($"Sleep is only legal from READY, not {record.State}.");

            Transition(record, StoragePlaneState.Quiescing, "sleep-requested", operationId, initiator, requireLease: true);
        }
    }

    public void MarkObserved(StoragePlaneState observed, string journalStep)
    {
        lock (_gate)
        {
            var record = _repository.Load();
            record.RealityReconciled = true;
            record.JournalStep = journalStep;
            if (record.State != observed)
                Transition(record, observed, journalStep, record.ActiveOperationId ?? OperationIdRules.Create().Value, record.LeaseHolder ?? _config.ControllerIdentity, requireLease: false);
            else
                _repository.Save(record);
        }
    }

    public void EnterReady(string operationId)
    {
        lock (_gate)
        {
            var record = _repository.Load();
            Transition(record, StoragePlaneState.Ready, "enter-ready", operationId, record.LeaseHolder ?? _config.ControllerIdentity, requireLease: false);
            record = _repository.Load();
            record.LastReadyAt = _clock.UtcNow;
            ReleaseLease(record);
            _repository.Save(record);
        }
    }

    public void EnterSleeping(string operationId)
    {
        lock (_gate)
        {
            var record = _repository.Load();
            Transition(record, StoragePlaneState.Sleeping, "enter-sleeping", operationId, record.LeaseHolder ?? _config.ControllerIdentity, requireLease: false);
            ApplyOwnedNoout(record, operationId);
            _repository.Save(record);
        }
    }

    public void EnterCold(string operationId)
    {
        lock (_gate)
        {
            var record = _repository.Load();
            Transition(record, StoragePlaneState.Cold, "enter-cold", operationId, record.LeaseHolder ?? _config.ControllerIdentity, requireLease: false);
            record = _repository.Load();
            ReleaseLease(record);
            _repository.Save(record);
        }
    }

    public void EnterFaulted(string reason)
    {
        lock (_gate)
        {
            var record = _repository.Load();
            var operationId = record.ActiveOperationId ?? OperationIdRules.Create().Value;
            Transition(record, StoragePlaneState.Faulted, reason, operationId, record.LeaseHolder ?? _config.ControllerIdentity, requireLease: false);
            record = _repository.Load();
            ReleaseLease(record);
            _repository.Save(record);
        }
    }

    public void ClearOwnedNoout()
    {
        lock (_gate)
        {
            var record = _repository.Load();
            foreach (var owned in record.OwnedNoout.ToArray())
                _noout.UnsetGroupNoout(owned.Scope);
            record.OwnedNoout.Clear();
            _repository.Save(record);
        }
    }

    public void RejectUnknownNooutClear(string scope)
    {
        var owned = _repository.Load().OwnedNoout.Any(record => record.Scope == scope);
        if (!owned)
            throw new InvalidOperationException($"Refusing to clear noout on '{scope}' because this controller does not own it.");
    }

    private void ApplyOwnedNoout(StoragePlaneRecord record, string operationId)
    {
        const string scope = "hdd-osds";
        if (record.OwnedNoout.Any(item => item.Scope == scope))
            return;

        _noout.SetGroupNoout(scope);
        record.OwnedNoout.Add(new NooutRecordDto
        {
            Scope = scope,
            OperationId = operationId,
            ControllerInstance = _config.ControllerIdentity,
            SetAt = _clock.UtcNow,
            PreviousState = false
        });
    }

    private void Transition(
        StoragePlaneRecord record,
        StoragePlaneState to,
        string step,
        string operationId,
        string initiator,
        bool requireLease)
    {
        if (record.State != to && !Allowed.Contains((record.State, to)))
            throw new InvalidOperationException($"Illegal storage-plane transition {record.State} → {to}.");

        if (requireLease)
            AcquireLease(record, operationId, initiator);

        record.State = to;
        record.JournalStep = step;
        record.LastTransitionAt = _clock.UtcNow;
        record.ActiveOperationId = operationId;
        _repository.Save(record);
    }

    private void AcquireLease(StoragePlaneRecord record, string operationId, string initiator)
    {
        if (record.LeaseId is not null && record.LeaseDeadline > _clock.UtcNow && record.ActiveOperationId != operationId)
            throw new InvalidOperationException("A storage-plane transition lease is already held.");

        record.LeaseId = Guid.NewGuid().ToString("N");
        record.LeaseHolder = initiator;
        record.ActiveOperationId = operationId;
        record.LeaseDeadline = _clock.UtcNow.AddMinutes(30);
    }

    private static void ReleaseLease(StoragePlaneRecord record)
    {
        record.LeaseId = null;
        record.LeaseHolder = null;
        record.LeaseDeadline = null;
    }

    private TransitionLeaseDto ExistingOrSynthetic(StoragePlaneRecord record, string operationId, string initiator)
    {
        if (record.LeaseId is not null)
            return GetLease()!;

        return new TransitionLeaseDto
        {
            LeaseId = "joined",
            Holder = initiator,
            OperationId = record.ActiveOperationId ?? operationId,
            AcquiredAt = record.LastTransitionAt ?? _clock.UtcNow,
            Deadline = _clock.UtcNow.AddMinutes(30)
        };
    }

    private StoragePlaneSnapshot ToSnapshot(StoragePlaneRecord record)
        => new()
        {
            State = record.State,
            LeaseHolder = record.LeaseHolder,
            ActiveOperationId = record.ActiveOperationId,
            JournalStep = record.JournalStep,
            ObservedAt = _clock.UtcNow,
            Trusted = record.RealityReconciled
        };

    private static void EnsureOperationId(string operationId)
    {
        if (!OperationIdRules.IsValid(operationId))
            throw new FormatException("Wake/sleep requires a valid operation ID.");
    }
}

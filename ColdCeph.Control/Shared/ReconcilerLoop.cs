using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ColdCeph.Control.Shared;

/// <summary>
/// A reconcile loop that survives a failing tick without erasing the evidence.
/// <para>
/// A bare <c>catch (Exception) { }</c> keeps the loop alive, which is required, but it also makes
/// a permanently failing reconciler indistinguishable from a healthy one: the operator sees a
/// plane stuck in WAKING and nothing anywhere says why. Every failure is logged and the most
/// recent one is kept, including failures a loop handles per host so that it can carry on with
/// the others.
/// </para>
/// </summary>
public abstract class ReconcilerLoop : BackgroundService
{
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private string? _lastError;
    private string? _tickError;
    private int _consecutiveFailures;

    protected ReconcilerLoop(ILogger logger)
    {
        _logger = logger;
    }

    protected virtual TimeSpan Interval => TimeSpan.FromSeconds(1);

    /// <summary>The most recent failure, or null when the last tick was wholly successful.</summary>
    public string? LastError
    {
        get
        {
            lock (_gate)
                return _lastError;
        }
    }

    public int ConsecutiveFailures
    {
        get
        {
            lock (_gate)
                return _consecutiveFailures;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            ReconcileOnce();
            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public void ReconcileOnce()
    {
        lock (_gate)
            _tickError = null;

        try
        {
            ReconcileBody();
        }
        catch (Exception exception)
        {
            Record($"{GetType().Name} tick failed", exception);
        }

        lock (_gate)
        {
            if (_tickError is null)
            {
                _lastError = null;
                _consecutiveFailures = 0;
                return;
            }

            _lastError = _tickError;
            _consecutiveFailures++;
        }
    }

    /// <summary>
    /// Records a failure the loop is deliberately continuing past — one unreachable node must not
    /// stop the others, but it must not vanish either.
    /// </summary>
    protected void Record(string context, Exception exception)
    {
        int failures;
        lock (_gate)
        {
            _tickError = $"{context}: {exception.Message}";
            failures = _consecutiveFailures + 1;
        }

        // The first is a warning; a run of them means the loop is not recovering on its own.
        if (failures == 1 || failures % 30 == 0)
            _logger.LogWarning(exception, "{Context} ({Failures} tick(s) in a row). The next tick retries.", context, failures);
    }

    protected abstract void ReconcileBody();
}

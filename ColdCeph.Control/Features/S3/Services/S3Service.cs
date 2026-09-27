using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Integrity.Controllers;
using ColdCeph.Control.Features.S3.Interfaces;
using ColdCeph.Control.Features.StoragePlane.Controllers;
using ColdCeph.Core.Features.Integrity.DTOs;
using ColdCeph.Core.Features.S3.DTOs;
using ColdCeph.Core.Features.StoragePlane.DTOs;

namespace ColdCeph.Control.Features.S3.Services;

public sealed class S3Service
{
    private readonly IRequestLedger _ledger;
    private readonly StoragePlaneController _plane;
    private readonly IntegrityController _integrity;
    private readonly IRgwProxy _proxy;
    private readonly ControlConfig _config;

    public S3Service(
        IRequestLedger ledger,
        StoragePlaneController plane,
        IntegrityController integrity,
        IRgwProxy proxy,
        ControlConfig config)
    {
        _ledger = ledger;
        _plane = plane;
        _integrity = integrity;
        _proxy = proxy;
        _config = config;
    }

    public S3PendingWorkDto GetPendingWork() => _ledger.Snapshot();

    public bool HasPendingWork() => _ledger.Snapshot().HasPendingWork;

    public async Task HandleAsync(HttpContext context)
    {
        var id = _ledger.BeginQueued();
        try
        {
            if (!await WaitUntilAdmitted(context))
                return;

            _ledger.Activate(id);
            await _proxy.ProxyAsync(context);
        }
        finally
        {
            _ledger.Complete(id);
        }
    }

    private async Task<bool> WaitUntilAdmitted(HttpContext context)
    {
        while (!context.RequestAborted.IsCancellationRequested)
        {
            if (_plane.GetState().State != StoragePlaneState.Ready)
            {
                if (_config.S3Mode == S3AdmissionMode.Retry)
                {
                    WriteRetryAfter(context);
                    return false;
                }

                try
                {
                    await Task.Delay(200, context.RequestAborted);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }

                continue;
            }

            var predicates = _integrity.GetIntegrity().Predicates;
            if (IsAdmitted(context.Request.Method, predicates))
                return true;

            WriteRetryAfter(context);
            return false;
        }

        return false;
    }

    private void WriteRetryAfter(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers.RetryAfter = _config.RetryAfterSeconds.ToString();
    }

    private bool IsAdmitted(string method, ReadinessPredicates predicates)
    {
        var readiness = _plane.GetReadiness(predicates.ReadReady, predicates.WriteReady);
        var write = method is "PUT" or "POST" or "DELETE" or "PATCH";
        return write ? readiness.ForwardWrites : readiness.ForwardReads;
    }
}

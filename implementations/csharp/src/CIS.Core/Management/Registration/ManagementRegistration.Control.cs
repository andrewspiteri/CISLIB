using System.Runtime.ExceptionServices;

namespace CIS.Management;

public sealed partial class ManagementRegistration
{
    private bool PrerequisitesReady()
    {
        lock (gate)
        {
            if (closing || !inventoryValid)
            {
                return false;
            }
        }
        foreach (IManagedStateReconciler reconciler in reconcilers)
        {
            if (!reconciler.IsReady)
            {
                return false;
            }
        }
        return true;
    }

    private async Task<bool> ConfirmControlAsync(CancellationToken caller)
    {
        try
        {
            return await ConfirmControlCoreAsync(caller).ConfigureAwait(false);
        }
        finally
        {
            lastControlAttempt = clock.GetTimestamp();
            controlAttempted = true;
        }
    }

    private async Task<bool> ConfirmControlCoreAsync(CancellationToken caller)
    {
        string id;
        lock (gate)
        {
            id = registrationId;
            controlAvailable = false;
        }
        ManagedControlAttempt attempt = admission.BeginAttempt(id, Guid.NewGuid().ToString("D"));
        var notification = new ControlCancellation(admission, attempt);
        ManagementControlConfirmation? confirmation;
        try
        {
            confirmation = await ReadAndReconcileControlAsync(attempt, caller, notification).ConfigureAwait(false);
            // Callback disposal/actual work completed before a confirmation can reopen admission.
            notification.ThrowIfFaulted();
        }
        catch (Exception error)
        {
            Exception retained = notification.Failure is { } secondary && !ReferenceEquals(error, secondary)
                ? new AggregateException("Control work and its cancellation notification faulted.", error, secondary) : error;
            ExceptionDispatchInfo.Capture(CloseAdmissionAfterFault(retained)).Throw();
            throw;
        }
        if (confirmation is null)
        {
            return false;
        }
        if (!admission.CompleteReconciliation(attempt, confirmation))
        {
            MarkFailure(admission.CaptureSnapshot().ReasonCode ?? "management.reconciliation.incomplete", closeAdmission: false);
            return false;
        }
        lock (gate)
        {
            controlHealthy = true;
            reconnectFailures = 0;
        }
        return true;
    }

    private async Task<ManagementControlConfirmation?> ReadAndReconcileControlAsync(ManagedControlAttempt attempt,
        CancellationToken caller, ControlCancellation notification)
    {
        using var budget = new CancellationTokenSource(options.ControlTimeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, stoppingToken, budget.Token);
        CancellationToken token = linked.Token;
        using CancellationTokenRegistration canceled = token.UnsafeRegister(_ => notification.Signal(
            budget.IsCancellationRequested && !caller.IsCancellationRequested && !stoppingToken.IsCancellationRequested
                ? "management.control.timeout" : "management.control.cancelled"), null);
        try
        {
            token.ThrowIfCancellationRequested();
            ManagementCallResult<ManagementControlConfirmation> result = await client.GetControlAsync(attempt.RegistrationId,
                attempt.AttemptId, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!result.Exchange.Success || result.Exchange.Value is not { } confirmation)
            {
                admission.Refuse(attempt, "management.control.unavailable");
                RefusedControl(result);
                return null;
            }
            lock (gate)
            {
                controlAvailable = true;
            }
            if (!admission.ValidateConfirmation(attempt, confirmation))
            {
                MarkFailure(admission.CaptureSnapshot().ReasonCode ?? "management.control.invalid_confirmation", closeAdmission: false);
                return null;
            }
            foreach (IManagedStateReconciler reconciler in reconcilers)
            {
                token.ThrowIfCancellationRequested();
                bool accepted = await (reconciler.ReconcileAsync(confirmation, token)
                    ?? throw new InvalidOperationException("A managed module reconciler returned no task.")).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (!accepted)
                {
                    admission.Refuse(attempt, "management.reconciliation.incomplete");
                    MarkFailure("management.reconciliation.incomplete", closeAdmission: false);
                    return null;
                }
            }
            token.ThrowIfCancellationRequested();
            return confirmation;
        }
        catch (OperationCanceledException error) when (token.IsCancellationRequested && error.CancellationToken == token)
        {
            if (caller.IsCancellationRequested || stoppingToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(caller);
            }
            admission.Refuse(attempt, "management.control.timeout");
            MarkFailure("management.control.timeout", closeAdmission: false);
            return null;
        }
    }

    private void RefusedControl(ManagementCallResult<ManagementControlConfirmation> result)
    {
        string? code = result.Problem?.Code;
        if (result.Exchange.Context.HttpStatusCode == 404 && code is "management.not_found" or "management.registration.not_found"
            || code is "management.registration.closed" or "management.registration.conflict")
        {
            Refused(result);
            return;
        }
        MarkFailure(result.Exchange.Context.FailureKind == CIS.RestClient.RestFailureKind.Timeout
            ? "management.control.timeout" : "management.control.unavailable", closeAdmission: false);
    }

    private Exception CloseAdmissionAfterFault(Exception original)
    {
        try
        {
            admission.CloseAfterFault();
        }
        catch (Exception secondary)
        {
            return new AggregateException("Management work and its admission cutoff faulted.", original, secondary);
        }
        return original;
    }

    private sealed class ControlCancellation(ManagedWorkAdmission admission, ManagedControlAttempt attempt)
    {
        private Exception? failure;

        internal Exception? Failure => Volatile.Read(ref failure);

        internal void Signal(string reason)
        {
            try
            {
                admission.Refuse(attempt, reason);
            }
            catch (Exception error)
            {
                Interlocked.CompareExchange(ref failure, error, null);
            }
        }

        internal void ThrowIfFaulted()
        {
            if (Failure is { } error)
            {
                ExceptionDispatchInfo.Capture(error).Throw();
            }
        }
    }
}

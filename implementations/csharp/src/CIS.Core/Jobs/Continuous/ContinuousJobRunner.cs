using CIS.Coordination;
using CIS.Transactions;

namespace CIS.Jobs;

/// <summary>Runs explicitly selected continuous duties as finite independently scoped iterations.</summary>
/// <remarks>Unexpected or uncertain business outcomes stop that local duty. Starting another duty is independent.
/// The runtime retains actual callback/scope ownership after cooperative cancellation or an authority deadline.</remarks>
public sealed class ContinuousJobRunner : IContinuousJobRunner
{
    private readonly JobRuntime runtime;

    /// <summary>Uses the shared runtime's actual key, capacity, settings and explicit security-scope owner.</summary>
    public ContinuousJobRunner(JobRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        this.runtime = runtime;
    }

    /// <inheritdoc />
    public async Task<ContinuousJobStartOutcome> StartAsync(string executionScope, string jobId, string resourceId,
        CancellationToken cancellationToken = default)
        => (await StartCoreAsync(executionScope, jobId, resourceId, null, cancellationToken).ConfigureAwait(false)).Outcome;

    internal Task<ContinuousJobActivationResult> StartAsync(JobScopeRequest target, long expectedActivationRevision,
        CancellationToken cancellationToken)
        => StartCoreAsync(target.ExecutionScope, target.JobId, target.ResourceId, expectedActivationRevision, cancellationToken);

    private async Task<ContinuousJobActivationResult> StartCoreAsync(string executionScope, string jobId, string resourceId,
        long? expectedActivationRevision, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await runtime.StartAsync(cancellationToken).ConfigureAwait(false);
        var target = new JobScopeRequest(executionScope, jobId, resourceId, JobExecutionMode.Continuous);
        JobAdmission admission = runtime.TryReserveContinuous(target, expectedActivationRevision);
        if (admission.Duty is not { } duty)
        {
            ContinuousJobStartOutcome outcome = admission.Outcome switch
            {
                JobAdmissionOutcome.Busy => ContinuousJobStartOutcome.AlreadyRunning,
                JobAdmissionOutcome.Unsupported => ContinuousJobStartOutcome.Unsupported,
                _ => ContinuousJobStartOutcome.Deferred
            };
            return new(outcome, admission.Outcome is JobAdmissionOutcome.Busy or JobAdmissionOutcome.Unsupported);
        }

        bool launched = false;
        try
        {
            using var starting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, runtime.AdmissionStopping, duty.StopRequested);
            JobAuthorizedScopeResult authorization = await runtime.OpenAuthorizedScopeAsync(duty, starting.Token).ConfigureAwait(false);
            if (authorization.Scope is not { } scope)
            {
                ContinuousJobStartOutcome outcome = authorization.Outcome switch
                {
                    JobAdmissionOutcome.Unauthorized => ContinuousJobStartOutcome.Unauthorized,
                    JobAdmissionOutcome.Unsupported => ContinuousJobStartOutcome.Unsupported,
                    _ => ContinuousJobStartOutcome.Deferred
                };
                return new(outcome, Consumed: true);
            }

            await scope.DisposeAsync().ConfigureAwait(false);
            starting.Token.ThrowIfCancellationRequested();
            if (scope.CleanupFailed)
            {
                duty.SetState(JobLocalState.Faulted, "jobs.start_scope_cleanup_failed");
                return new(ContinuousJobStartOutcome.Deferred, Consumed: true, Failed: true);
            }

            launched = true;
            _ = RunDutyAsync(duty);
            return new(ContinuousJobStartOutcome.Started, Consumed: true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
            && (duty.StopRequested.IsCancellationRequested || runtime.AdmissionStopping.IsCancellationRequested))
        {
            return new(ContinuousJobStartOutcome.Deferred, Consumed: true);
        }
        finally
        {
            if (!launched)
            {
                await duty.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public Task StopAsync(string executionScope, string jobId, string resourceId, CancellationToken cancellationToken = default)
        => runtime.StopDutyAsync(new(executionScope, jobId, resourceId, JobExecutionMode.Continuous), cancellationToken);

    /// <inheritdoc />
    public IReadOnlyList<JobRuntimeSnapshot> CaptureSnapshot(int offset = 0, int? pageSize = null)
        => runtime.CaptureSnapshot(offset, pageSize);

    private async Task RunDutyAsync(JobDutyReservation duty)
    {
        try
        {
            using var admission = CancellationTokenSource.CreateLinkedTokenSource(duty.StopRequested, runtime.AdmissionStopping);
            while (!duty.IsDraining)
            {
                ManagedCoordinationResult acquired = await runtime.AcquireAsync(duty, admission.Token).ConfigureAwait(false);
                if (acquired.Lease is { } lease)
                {
                    await RunOwnedAsync(duty, lease).ConfigureAwait(false);
                    return;
                }

                if (acquired.Outcome is not (CoordinationAcquireOutcome.Busy or CoordinationAcquireOutcome.Unavailable
                    or CoordinationAcquireOutcome.CapacityExceeded))
                {
                    duty.SetState(JobLocalState.Faulted, acquired.Outcome == CoordinationAcquireOutcome.Indeterminate
                        ? "jobs.acquisition_indeterminate" : "jobs.acquisition_refused");
                    return;
                }

                duty.SetState(JobLocalState.Standby, acquired.ReasonCode);
                JobConfigurationSnapshot current = runtime.Configuration;
                if (duty.IsDraining || !current.Jobs.TryGetValue((duty.Target.ExecutionScope, duty.Target.JobId), out JobSettings? settings))
                {
                    return;
                }

                await Task.Delay(settings.Execution.AcquisitionRetryDelay, runtime.Clock, admission.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (duty.StopRequested.IsCancellationRequested || runtime.AdmissionStopping.IsCancellationRequested)
        {
            // Admission cancellation initiates drain; it is not a statement about an admitted business commit.
        }
        catch (Exception error) when (IsOrdinary(error))
        {
            duty.SetState(JobLocalState.Faulted, "jobs.duty_failed");
        }
        finally
        {
            await runtime.ReleaseLeaseAsync(duty).ConfigureAwait(false);
            await duty.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task RunOwnedAsync(JobDutyReservation duty, ICoordinationLease lease)
    {
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(duty.StopRequested, lease.OwnershipLost);
        while (!duty.IsDraining)
        {
            if (lease.State != CoordinationLeaseState.Owned)
            {
                duty.SetState(JobLocalState.Lost, "jobs.ownership_lost");
                return;
            }

            long version = runtime.ChangeVersion;
            JobInvocationReservation? invocation = runtime.TryReserveIteration(duty);
            if (invocation is null)
            {
                try
                {
                    await runtime.WaitForChangeAsync(version, waiting.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (waiting.IsCancellationRequested) { return; }
                continue;
            }

            ContinuousIterationResult? result = await InvokeIterationAsync(duty, lease, invocation).ConfigureAwait(false);
            if (result is null)
            {
                if (!invocation.Entered && !duty.IsDraining && !runtime.IsFaulted(duty)
                    && lease.State == CoordinationLeaseState.Owned && runtime.IsEnabled(duty.Target))
                {
                    continue;
                }

                return;
            }

            if (result.Outcome is ContinuousIterationOutcome.Failed or ContinuousIterationOutcome.Indeterminate)
            {
                return;
            }

            if (duty.IsDraining)
            {
                return;
            }

            if (lease.State != CoordinationLeaseState.Owned)
            {
                duty.SetState(JobLocalState.Lost, result.Completion?.Outcome == TransactionOutcome.Committed
                    ? "jobs.committed_ownership_lost" : "jobs.iteration_ownership_lost");
                return;
            }

            if (result.Outcome == ContinuousIterationOutcome.Idle)
            {
                duty.SetState(JobLocalState.Idle, "jobs.idle");
                try
                {
                    await Task.Delay(invocation.Settings.IdleDelay, runtime.Clock, waiting.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (waiting.IsCancellationRequested) { return; }
            }
            else
            {
                // Existing capacity waiters were signalled when this invocation actually settled.
                await Task.Yield();
            }
        }
    }

    private async Task<ContinuousIterationResult?> InvokeIterationAsync(JobDutyReservation duty, ICoordinationLease lease,
        JobInvocationReservation invocation)
    {
        JobAuthorizedScope? scope = null;
        ContinuousIterationResult? result = null;
        string observation = "failed";
        try
        {
            using var preparing = CancellationTokenSource.CreateLinkedTokenSource(duty.StopRequested, lease.OwnershipLost);
            JobAuthorizedScopeResult authorization = await runtime.OpenAuthorizedScopeAsync(duty, preparing.Token).ConfigureAwait(false);
            scope = authorization.Scope;
            if (scope is null)
            {
                if (!duty.IsDraining)
                {
                    duty.SetState(JobLocalState.Faulted, "jobs.iteration_scope_refused");
                }
                return null;
            }

            if (lease.State != CoordinationLeaseState.Owned || !runtime.TryEnterInvocation(invocation))
            {
                return null;
            }

            using var timeout = invocation.EffectiveTimeout is { } selected
                ? new CancellationTokenSource(selected, runtime.Clock) : new CancellationTokenSource();
            using var callback = CancellationTokenSource.CreateLinkedTokenSource(lease.OwnershipLost, timeout.Token);
            var context = new JobExecutionContext(duty.Binding.Definition, duty.Target.ResourceId, lease, scope.Services, invocation.Settings);
            try
            {
                result = await duty.Binding.InvokeContinuousAsync(context, callback.Token).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The continuous handler returned no finite iteration result.");
                observation = result.Outcome switch
                {
                    ContinuousIterationOutcome.Indeterminate => "indeterminate",
                    ContinuousIterationOutcome.Failed => "failed",
                    _ => "success"
                };
                if (result.Outcome is ContinuousIterationOutcome.Failed or ContinuousIterationOutcome.Indeterminate)
                {
                    duty.SetState(JobLocalState.Faulted, result.Outcome == ContinuousIterationOutcome.Indeterminate
                        ? "jobs.business_indeterminate" : result.ReasonCode ?? "jobs.iteration_failed");
                }
            }
            catch (OperationCanceledException) when (callback.IsCancellationRequested)
            {
                bool lost = lease.OwnershipLost.IsCancellationRequested;
                observation = lost ? "ownership_lost" : "cancelled";
                duty.SetState(lost ? JobLocalState.Lost : JobLocalState.Faulted,
                    lost ? "jobs.callback_ownership_lost" : "jobs.callback_timeout");
            }
        }
        catch (OperationCanceledException) when (duty.StopRequested.IsCancellationRequested || lease.OwnershipLost.IsCancellationRequested)
        {
            observation = lease.OwnershipLost.IsCancellationRequested ? "ownership_lost" : "cancelled";
        }
        catch (Exception error) when (IsOrdinary(error))
        {
            duty.SetState(JobLocalState.Faulted, "jobs.callback_failed");
        }
        finally
        {
            if (scope is not null)
            {
                await scope.DisposeAsync().ConfigureAwait(false);
                if (scope.CleanupFailed)
                {
                    string code = result?.Outcome == ContinuousIterationOutcome.Indeterminate
                        ? "jobs.business_indeterminate_scope_cleanup_failed"
                        : result?.Completion?.Outcome == TransactionOutcome.Committed
                            ? "jobs.committed_scope_cleanup_failed" : "jobs.scope_cleanup_failed";
                    duty.SetState(JobLocalState.Faulted, code);
                }
            }

            invocation.Complete(observation);
        }

        if (scope?.CleanupFailed == true)
        {
            return result?.Outcome == ContinuousIterationOutcome.Indeterminate ? result : null;
        }

        return result;
    }

    private static bool IsOrdinary(Exception error) => error is not OutOfMemoryException
        and not StackOverflowException and not AccessViolationException;
}

internal readonly record struct ContinuousJobActivationResult(ContinuousJobStartOutcome Outcome, bool Consumed, bool Failed = false);

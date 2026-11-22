using CIS.Coordination;
using CIS.Messaging;

namespace CIS.Jobs;

/// <summary>Executes one trusted admitted occurrence through bounded local admission and actual scoped cleanup.</summary>
/// <remarks>The selected source owns durable acknowledgement and retry. This adapter does not calculate recurrence,
/// replay business callbacks, or infer rollback from local cancellation or ownership loss.</remarks>
public sealed class ScheduledJobDispatcher : IScheduledJobDispatcher
{
    private readonly JobRuntime runtime;

    /// <summary>Uses the runtime's fixed source, catalog, identity and shared actual capacity without performing I/O.</summary>
    public ScheduledJobDispatcher(JobRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        this.runtime = runtime;
    }

    /// <inheritdoc />
    public async Task<ScheduledJobResult> DispatchAsync(AdmittedJobRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        cancellationToken.ThrowIfCancellationRequested();
        using JobPendingDispatch? pending = runtime.TryReservePending();
        if (pending is null)
        {
            return new(ScheduledJobOutcome.Deferred, "jobs.pending_capacity_or_stopping");
        }

        using var admissionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, runtime.AdmissionStopping);
        try
        {
            IJobRunSource? source = runtime.RunSource;
            JobHandlerBinding? binding = runtime.Catalog.Find(run.ExecutionScope, run.JobId);
            if (binding?.InputContract is null || !binding.Definition.SupportedModes.Contains(JobExecutionMode.Scheduled))
            {
                return new(ScheduledJobOutcome.Unsupported, "jobs.target.unsupported");
            }

            var target = new JobScopeRequest(run.ExecutionScope, run.JobId, run.ResourceId, JobExecutionMode.Scheduled);
            if (!runtime.IsEnabled(target))
            {
                return new(ScheduledJobOutcome.Deferred, "jobs.local_enablement.closed");
            }

            if (source is null || source.Capabilities != JobRunSourceCapabilities.ExternallyAdmittedOccurrences
                || !string.Equals(source.SourceId, run.SourceId, StringComparison.Ordinal)
                || run.Settings.Schedule is null || run.Input.Envelope.Kind != MessageKind.Command
                || run.Input.Envelope.Operation != run.Operation || run.Input.Envelope.Contract != binding.InputContract
                || run.Input.Length > MessagingLimits.MaximumSupportedPayloadBytes + MessagingLimits.MaximumMetadataBytes)
            {
                return new(ScheduledJobOutcome.Unsupported, "jobs.admission.unsupported");
            }

            JobAdmissionValidation validation = await source.ValidateAsync(run, admissionCancellation.Token).ConfigureAwait(false);
            admissionCancellation.Token.ThrowIfCancellationRequested();
            if (validation is null || !validation.Admitted)
            {
                return new(ScheduledJobOutcome.Unsupported, "jobs.admission.not_validated");
            }

            JobAuthorizedScopeResult authorization = await runtime.OpenAuthorizedScopeAsync(target, admissionCancellation.Token).ConfigureAwait(false);
            if (authorization.Scope is not { } initialScope)
            {
                return AdmissionResult(authorization.Outcome);
            }

            await using (initialScope.ConfigureAwait(false))
            {
                JobAdmission admission = runtime.TryReserveScheduled(run, pending);
                if (admission.Duty is not { } duty || admission.Invocation is not { } invocation)
                {
                    return AdmissionResult(admission.Outcome);
                }

                return await ExecuteAdmittedAsync(run, duty, invocation, initialScope, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (admissionCancellation.IsCancellationRequested)
        {
            return new(cancellationToken.IsCancellationRequested ? ScheduledJobOutcome.Cancelled : ScheduledJobOutcome.Deferred,
                "jobs.admission.cancelled");
        }
        catch (Exception error) when (IsOrdinary(error))
        {
            return new(ScheduledJobOutcome.Failed, "jobs.admission.failed");
        }
    }

    private async Task<ScheduledJobResult> ExecuteAdmittedAsync(AdmittedJobRun run, JobDutyReservation duty,
        JobInvocationReservation invocation, JobAuthorizedScope initialScope, CancellationToken caller)
    {
        ScheduledJobResult result = new(ScheduledJobOutcome.Failed, "jobs.invocation.failed");
        JobAuthorizedScope? workScope = null;
        await using (duty.ConfigureAwait(false))
        {
            try
            {
                using var acquiring = CancellationTokenSource.CreateLinkedTokenSource(caller, runtime.AdmissionStopping, duty.StopRequested);
                ManagedCoordinationResult acquired;
                try
                {
                    acquired = await runtime.AcquireAsync(duty, initialScope, acquiring.Token).ConfigureAwait(false);
                }
                finally
                {
                    await initialScope.DisposeAsync().ConfigureAwait(false);
                }
                if (acquired.Outcome != CoordinationAcquireOutcome.Acquired)
                {
                    result = AcquisitionResult(acquired);
                    return result;
                }

                JobAuthorizedScopeResult authorization = await runtime.OpenAuthorizedScopeAsync(duty, acquiring.Token).ConfigureAwait(false);
                workScope = authorization.Scope;
                if (workScope is null)
                {
                    result = AdmissionResult(authorization.Outcome);
                    return result;
                }

                ICoordinationLease lease = duty.Lease!;
                CoordinationLeaseState leaseState = lease.State;
                if (leaseState is CoordinationLeaseState.Lost or CoordinationLeaseState.Released)
                {
                    result = new(ScheduledJobOutcome.OwnershipLost, "jobs.ownership_lost_before_callback");
                    return result;
                }

                if (caller.IsCancellationRequested)
                {
                    result = new(ScheduledJobOutcome.Cancelled, "jobs.cancelled_before_callback");
                    return result;
                }

                if (leaseState != CoordinationLeaseState.Owned || duty.IsDraining || !runtime.TryEnterInvocation(invocation))
                {
                    result = new(ScheduledJobOutcome.Deferred, "jobs.callback_admission.closed");
                    return result;
                }

                using var timeout = invocation.EffectiveTimeout is { } duration
                    ? new CancellationTokenSource(duration, runtime.Clock) : new CancellationTokenSource();
                using var callbackCancellation = CancellationTokenSource.CreateLinkedTokenSource(caller, lease.OwnershipLost, timeout.Token);
                var context = new JobExecutionContext(duty.Binding.Definition, run.ResourceId, lease, workScope.Services,
                    invocation.Settings, run);
                try
                {
                    result = await duty.Binding.InvokeScheduledAsync(context, callbackCancellation.Token).ConfigureAwait(false)
                        ?? new(ScheduledJobOutcome.Failed, "jobs.handler.no_result");
                }
                catch (OperationCanceledException) when (callbackCancellation.IsCancellationRequested)
                {
                    result = new(lease.State is CoordinationLeaseState.Lost or CoordinationLeaseState.Released
                        ? ScheduledJobOutcome.OwnershipLost : ScheduledJobOutcome.Cancelled, "jobs.callback.cancelled");
                }
                catch (Exception error) when (IsOrdinary(error))
                {
                    result = new(ScheduledJobOutcome.Failed, "jobs.callback.failed");
                }

                // Preserve the actual original result, including Committed or Unknown, after loss or cancellation.
                return result;
            }
            catch (OperationCanceledException) when (caller.IsCancellationRequested || runtime.AdmissionStopping.IsCancellationRequested
                || duty.StopRequested.IsCancellationRequested)
            {
                result = new(caller.IsCancellationRequested ? ScheduledJobOutcome.Cancelled : ScheduledJobOutcome.Deferred,
                    "jobs.acquisition.cancelled");
                return result;
            }
            catch (Exception error) when (IsOrdinary(error))
            {
                result = new(ScheduledJobOutcome.Failed, "jobs.execution.failed");
                return result;
            }
            finally
            {
                if (workScope is not null)
                {
                    await workScope.DisposeAsync().ConfigureAwait(false);
                }

                if (invocation.Entered)
                {
                    try
                    {
                        PublishCompletion(duty, result);
                    }
                    catch (Exception error) when (IsOrdinary(error))
                    {
                        runtime.RecordCleanupDiagnostic("jobs.completion_diagnostic_failed");
                    }
                }

                invocation.Complete(ObservationOutcome(result));
                await runtime.ReleaseLeaseAsync(duty).ConfigureAwait(false);
            }
        }
    }

    private static ScheduledJobResult AdmissionResult(JobAdmissionOutcome outcome) => outcome switch
    {
        JobAdmissionOutcome.Busy => new(ScheduledJobOutcome.Busy, "jobs.local_key.busy"),
        JobAdmissionOutcome.Unauthorized => new(ScheduledJobOutcome.Unauthorized, "jobs.authorization.denied"),
        JobAdmissionOutcome.Unsupported => new(ScheduledJobOutcome.Unsupported, "jobs.target.unsupported"),
        _ => new(ScheduledJobOutcome.Deferred, "jobs.local_capacity_or_enablement")
    };

    private static ScheduledJobResult AcquisitionResult(ManagedCoordinationResult result) => result.Outcome switch
    {
        CoordinationAcquireOutcome.Busy => new(ScheduledJobOutcome.Busy, result.ReasonCode),
        CoordinationAcquireOutcome.Unauthorized => new(ScheduledJobOutcome.Unauthorized, result.ReasonCode),
        CoordinationAcquireOutcome.InvalidRequest => new(ScheduledJobOutcome.Unsupported, result.ReasonCode),
        CoordinationAcquireOutcome.Indeterminate => new(ScheduledJobOutcome.Indeterminate, result.ReasonCode),
        _ => new(ScheduledJobOutcome.Deferred, result.ReasonCode)
    };

    private static string ObservationOutcome(ScheduledJobResult result) => result.Outcome switch
    {
        ScheduledJobOutcome.Completed or ScheduledJobOutcome.PreviouslyCompleted => "success",
        ScheduledJobOutcome.Cancelled => "cancelled",
        ScheduledJobOutcome.OwnershipLost => "ownership_lost",
        ScheduledJobOutcome.Indeterminate => "indeterminate",
        _ => "failed"
    };

    private static void PublishCompletion(JobDutyReservation duty, ScheduledJobResult result)
    {
        string code = result.Outcome switch
        {
            ScheduledJobOutcome.Completed => "jobs.completed",
            ScheduledJobOutcome.PreviouslyCompleted => "jobs.previously_completed",
            ScheduledJobOutcome.Indeterminate => "jobs.indeterminate",
            ScheduledJobOutcome.Cancelled => "jobs.cancelled",
            ScheduledJobOutcome.OwnershipLost => "jobs.ownership_lost",
            ScheduledJobOutcome.Unsupported => "jobs.unsupported",
            ScheduledJobOutcome.Unauthorized => "jobs.unauthorized",
            ScheduledJobOutcome.Busy => "jobs.busy",
            ScheduledJobOutcome.Deferred => "jobs.deferred",
            _ => "jobs.failed"
        };
        if (result.Outcome is ScheduledJobOutcome.Failed or ScheduledJobOutcome.Indeterminate)
        {
            duty.SetState(JobLocalState.Faulted, code);
        }
        else if (duty.Lease?.State == CoordinationLeaseState.Lost)
        {
            if (result.Outcome == ScheduledJobOutcome.Completed)
            {
                code = "jobs.completed_after_ownership_loss";
            }
            else if (result.Outcome == ScheduledJobOutcome.PreviouslyCompleted)
            {
                code = "jobs.previously_completed_after_ownership_loss";
            }

            duty.SetState(JobLocalState.Lost, code);
        }
        else
        {
            duty.SetState(JobLocalState.Draining, code);
        }
    }

    private static bool IsOrdinary(Exception error) => error is not OutOfMemoryException
        and not StackOverflowException and not AccessViolationException;
}

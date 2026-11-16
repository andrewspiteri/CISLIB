namespace CIS.Heartbeat;

// One accepted execution, including queued ownership and every awaited phase.
internal sealed class HeartbeatExecution
{
    private readonly HeartbeatRuntime runtime;
    private readonly long admittedTimestamp;
    private HeartbeatBinding? binding;
    private IHeartbeatSubscriber? target;
    private HeartbeatNotification facts;
    private readonly HeartbeatNotification admissionFact;
    private HeartbeatNotification? dispatchFact;
    private HeartbeatPhase? lastPhase;
    private HeartbeatResult? result;
    private bool userStarted;
    private DateTimeOffset? conditionCompleted;
    private readonly TaskCompletionSource dispatchPublished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly HeartbeatRegistration Registration;
    internal HeartbeatOwner? Owner;
    internal bool HasSlot;
    internal int WrapperStarted;

    internal HeartbeatExecution(HeartbeatRuntime runtime, HeartbeatRegistration registration, HeartbeatBinding binding,
        IHeartbeatSubscriber target, HeartbeatOwner owner, long executionId, long timestamp)
    {
        this.runtime = runtime;
        Registration = registration;
        this.binding = binding;
        this.target = target;
        Owner = owner;
        admittedTimestamp = timestamp;
        DateTimeOffset admitted = runtime.ReadUtcNow();
        DateTimeOffset eligible;
        try { eligible = registration.OriginUtc + registration.EffectiveDelay; }
        catch (ArgumentOutOfRangeException error)
        {
            // UTC is correlation only; its representable range cannot invalidate
            // an otherwise valid monotonic delay or strand admission.
            eligible = admitted;
            runtime.FailInfrastructure("Heartbeat.CorrelationTime", error);
        }
        facts = new(HeartbeatEvent.Admitted, registration.Id, admitted)
        {
            ExecutionId = executionId,
            EligibleAtUtc = eligible,
            AdmittedAtUtc = admitted,
            QueueDelayMilliseconds = Math.Max(0, runtime.Milliseconds(registration.Origin, timestamp)
                - runtime.Milliseconds(0, registration.Delay)),
            ErrorHandlingStatus = HeartbeatStep.NotStarted,
            CleanupStatus = HeartbeatStep.NotStarted
        };
        admissionFact = facts;
    }

    internal void ReportAdmission()
    {
        runtime.Report(Registration, admissionFact with { Kind = HeartbeatEvent.Eligible, OccurredAtUtc = admissionFact.EligibleAtUtc!.Value });
        runtime.Report(Registration, admissionFact);
    }

    internal void MarkDispatched(long timestamp)
    {
        facts = facts with
        {
            DispatchedAtUtc = runtime.ReadUtcNow(),
            DispatchDelayMilliseconds = runtime.Milliseconds(admittedTimestamp, timestamp)
        };
        dispatchFact = facts with { Kind = HeartbeatEvent.Dispatched, OccurredAtUtc = facts.DispatchedAtUtc!.Value };
    }

    internal void ReportDispatch()
        => runtime.Report(Registration, dispatchFact!);

    internal void PublishDispatch() => dispatchPublished.TrySetResult();
    internal void ClearDispatch()
    {
        facts = facts with { DispatchedAtUtc = null, DispatchDelayMilliseconds = null };
        dispatchFact = null;
    }

    internal async Task RunAsync()
    {
        if (HasSlot) { await dispatchPublished.Task.ConfigureAwait(false); }
        using IDisposable execution = HeartbeatExecutionContext.Enter(runtime, Registration.Id, Owner!.Id);
        try
        {
            if (binding!.Condition is not null)
            {
                bool? allowed = await InvokeAsync(HeartbeatPhase.Condition).ConfigureAwait(false);
                if (allowed == false) { result = HeartbeatResult.Skipped; }
                else if (allowed == true) { await InvokeCallbackAsync().ConfigureAwait(false); }
            }
            else { await InvokeCallbackAsync().ConfigureAwait(false); }
        }
        catch (Exception error)
        {
            // Infrastructure seams are trusted, but a fault must still leave a
            // tracked terminal task and must not abandon other executions.
            runtime.FailInfrastructure("Heartbeat.ExecutionBoundary", error);
            if (userStarted) { result = HeartbeatResult.Failed; facts = facts with { OriginalError = facts.OriginalError ?? error }; }
        }
        if (facts.ErrorHandlingStatus == HeartbeatStep.NotStarted) { facts = facts with { ErrorHandlingStatus = HeartbeatStep.NotApplicable }; }
        await CleanupAsync().ConfigureAwait(false);
        if (result.HasValue)
        {
            runtime.Report(Registration, facts with
            {
                Kind = HeartbeatEvent.Outcome,
                OccurredAtUtc = facts.WorkCompletedAtUtc!.Value,
                SourcePhase = lastPhase,
                Outcome = result
            });
        }
        await runtime.FlushAsync(Registration).ConfigureAwait(false);
    }

    private async Task InvokeCallbackAsync()
    {
        bool? callback = await InvokeAsync(HeartbeatPhase.Callback).ConfigureAwait(false);
        if (callback.HasValue) { result = callback.Value ? HeartbeatResult.Succeeded : HeartbeatResult.Failed; }
    }

    private async Task<bool?> InvokeAsync(HeartbeatPhase phase)
    {
        if (!runtime.TryStart(this))
        {
            Suppress(phase);
            return null;
        }
        userStarted = true;
        lastPhase = phase;
        long started = runtime.ReadTimestamp();
        DateTimeOffset utc = runtime.ReadUtcNow();
        facts = phase == HeartbeatPhase.Condition ? facts with { ConditionStartedAtUtc = utc } : facts with { CallbackStartedAtUtc = utc };
        runtime.Report(Registration, facts with
        {
            Kind = phase == HeartbeatPhase.Condition ? HeartbeatEvent.ConditionStarted : HeartbeatEvent.CallbackStarted,
            OccurredAtUtc = utc
        });
        Exception? originating = null;
        bool? value = null;
        try
        {
            Task<bool>? task = phase == HeartbeatPhase.Condition
                ? binding!.Condition!(binding.ExecutionCancellation)
                : target!.OnElapsedAsync(binding!.ExecutionCancellation);
            if (task is null) { throw new InvalidOperationException("A Heartbeat user method returned a null task."); }
            value = await task.ConfigureAwait(false);
            facts = phase == HeartbeatPhase.Condition ? facts with { ConditionResult = value } : facts with { CallbackResult = value };
        }
        catch (OperationCanceledException error) when (binding!.ExecutionCancellation.CanBeCanceled
            && binding.ExecutionCancellation.IsCancellationRequested && error.CancellationToken == binding.ExecutionCancellation)
        {
            result = HeartbeatResult.ExpectedCancellation;
            facts = facts with { ExpectedCancellationPhase = phase };
        }
        catch (Exception error)
        {
            originating = error;
            result = HeartbeatResult.Failed;
            facts = facts with { OriginalError = error };
            if (error is ObjectDisposedException) { runtime.Retire(Registration); }
        }
        finally
        {
            double elapsed = runtime.Milliseconds(started, runtime.ReadTimestamp());
            DateTimeOffset completed = runtime.ReadUtcNow();
            if (phase == HeartbeatPhase.Condition) { conditionCompleted = completed; }
            if (phase == HeartbeatPhase.Callback || value != true) { facts = facts with { WorkCompletedAtUtc = completed }; }
            facts = phase == HeartbeatPhase.Condition
                ? facts with { ConditionDurationMilliseconds = elapsed }
                : facts with { CallbackDurationMilliseconds = elapsed };
        }
        if (originating is not null) { await HandleErrorAsync(originating).ConfigureAwait(false); }
        return value;
    }

    private async Task HandleErrorAsync(Exception originating)
    {
        // Closure does not invalidate an already acquired hold. A dependency's
        // disposal exception alone does not prove the subscriber itself unusable.
        if (Registration.Unusable)
        {
            facts = facts with { ErrorHandlingStatus = HeartbeatStep.NotInvoked };
            return;
        }
        long started = runtime.ReadTimestamp();
        DateTimeOffset utc = runtime.ReadUtcNow();
        facts = facts with { ErrorHandlingStartedAtUtc = utc, ErrorHandlingStatus = HeartbeatStep.Running };
        runtime.Report(Registration, facts with { Kind = HeartbeatEvent.ErrorHandlingStarted, OccurredAtUtc = utc });
        try
        {
            Task? task = target!.OnErrorAsync(originating);
            if (task is null) { throw new InvalidOperationException("A Heartbeat error handler returned a null task."); }
            await task.ConfigureAwait(false);
            facts = facts with { ErrorHandlingStatus = HeartbeatStep.Succeeded };
        }
        catch (Exception error)
        {
            facts = facts with { ErrorHandlingStatus = HeartbeatStep.Failed, ErrorHandlingError = error };
            if (error is ObjectDisposedException) { runtime.Retire(Registration); }
        }
        DateTimeOffset completed = runtime.ReadUtcNow();
        facts = facts with
        {
            ErrorHandlingCompletedAtUtc = completed,
            ErrorHandlingDurationMilliseconds = runtime.Milliseconds(started, runtime.ReadTimestamp())
        };
        runtime.Report(Registration, facts with { Kind = HeartbeatEvent.ErrorHandlingCompleted, OccurredAtUtc = completed });
    }

    private void Suppress(HeartbeatPhase phase)
    {
        DateTimeOffset utc = runtime.ReadUtcNow();
        facts = facts with { SuppressedAtUtc = utc, SuppressedPhase = phase, TargetUnavailable = Registration.Unusable,
            WorkCompletedAtUtc = conditionCompleted };
        runtime.Report(Registration, facts with { Kind = HeartbeatEvent.Suppressed, OccurredAtUtc = utc });
    }

    private async Task CleanupAsync()
    {
        if (binding!.Cleanup is null) { facts = facts with { CleanupStatus = HeartbeatStep.NotApplicable }; return; }
        long started = runtime.ReadTimestamp();
        try
        {
            await binding.Cleanup().ConfigureAwait(false);
            facts = facts with { CleanupStatus = HeartbeatStep.Succeeded };
        }
        catch (Exception error)
        {
            facts = facts with { CleanupStatus = HeartbeatStep.Failed, CleanupError = error };
            if (userStarted) { result = HeartbeatResult.Failed; }
            runtime.FailInfrastructure("Heartbeat.ExecutionCleanup", error);
            runtime.Report(Registration, facts with { Kind = HeartbeatEvent.IntegrationFault, OccurredAtUtc = runtime.ReadUtcNow(), IntegrationError = error });
        }
        DateTimeOffset completed = runtime.ReadUtcNow();
        facts = facts with { CleanupCompletedAtUtc = completed, CleanupDurationMilliseconds = runtime.Milliseconds(started, runtime.ReadTimestamp()) };
        runtime.Report(Registration, facts with { Kind = HeartbeatEvent.CleanupCompleted, OccurredAtUtc = completed });
    }

    internal HeartbeatNotification DetachSettlement(long timestamp)
    {
        DateTimeOffset utc = runtime.ReadUtcNow();
        return facts with
        {
            Kind = HeartbeatEvent.Settled,
            OccurredAtUtc = utc,
            SettledAtUtc = utc,
            TotalDurationMilliseconds = (facts.QueueDelayMilliseconds ?? 0) + runtime.Milliseconds(admittedTimestamp, timestamp),
            OriginalError = null,
            ErrorHandlingError = null,
            CleanupError = null,
            IntegrationError = null
        };
    }

    internal void Release()
    {
        target = null;
        binding = null;
        Owner = null;
        facts = facts with { OriginalError = null, ErrorHandlingError = null, CleanupError = null, IntegrationError = null };
    }
}

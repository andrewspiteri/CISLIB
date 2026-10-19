using CIS.Logging.Events;
using CIS.Logging.Runtime;

namespace CIS.Logging;

public sealed partial class CoreLogRuntime
{
    private IncomingEventQueue? incoming;
    private Task? pipelineCompletion;
    private EventProcessing? processing;
    private readonly AtomicLogCounter workerInvalid = new(), workerTruncated = new();

    private LogWriteResult WriteIncoming(string component, CoreLogLevel level, LogEventData? data, Func<LogEventData>? factory)
    {
        if (IsAdmissionClosed || incoming!.IsClosed) { return Stopped(isTruncated: false); }
        if (!outputs.Any(output => Interested(output, level))) { return FinishIncomingEarly(level, LogWriteOutcome.Filtered); }
        if (!outputs.Any(output => Interested(output, level) && !output.Admission.IsFaulted))
        { return FinishIncomingEarly(level, LogWriteOutcome.Rejected); }
        if (factory is not null)
        {
            try { data = factory(); }
            catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException))
            { return Invalid("FactoryFailed"); }
        }
        if (data is null) { return Invalid("InvalidFactoryResult"); }
        if (data.Interpolation?.ErrorCode is { } interpolationError) { return Invalid(interpolationError); }
        CapturedEventFacts facts;
        try { facts = EventProjection.Capture(metadata, Scopes.Capture(), component, level, data, timeProvider)!; }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException))
        { return Invalid(CaptureError(data.EventName, data.Message, data.MessageTemplate, data.OperationId)); }
        if (facts.IsTruncated) { truncatedEventCount.Increment(); }
        long? sequence = incoming.TryAdmit(level, facts);
        return sequence is null ? Stopped(facts.IsTruncated) :
            new LogWriteResult(LogWriteOutcome.Accepted, [], isTruncated: facts.IsTruncated, incomingSequence: sequence);
    }

    private LogWriteResult FinishIncomingEarly(CoreLogLevel level, LogWriteOutcome outcome)
    {
        var observations = new LogOutputWriteResult[outputs.Length];
        for (int index = 0; index < outputs.Length; index++)
        {
            Output output = outputs[index];
            bool interested = Interested(output, level);
            if (interested) { output.CallerUnavailableCounter.Increment(); }
            else { output.CallerFilteredCounter.Increment(); }
            observations[index] = new(output.Name, interested ? LogOutputWriteStatus.Faulted : LogOutputWriteStatus.Filtered, null);
        }
        return new LogWriteResult(outcome, observations);
    }

    // Invoked exactly once by unpublished factory composition, after output initialization succeeds.
    internal void StartIncoming(int capacity, Func<Func<Task>, Task>? launch = null, EventProcessing? processing = null, LoggingOptions? options = null)
    {
        if (incoming is not null || pipelineCompletion is not null) { throw new InvalidOperationException("Runtime startup is sealed."); }
        this.processing = processing;
        incoming = new IncomingEventQueue(capacity, admissionGate, timeProvider, PrepareIncoming, RegisterFlushBoundaryAt, launch, options);
    }

    private RuntimeFlushBoundary RegisterFlushBoundaryAt(long timestamp)
    {
        RuntimeFlushBoundary boundary = RegisterFlushBoundary(timestamp);
        // Caller early counts are frontend atomics, separate from backend output admission counts.
        return new(boundary.BoundaryCapturedAtUtc, workerInvalid.Value, workerTruncated.Value, null, null, boundary.Entries.ToArray());
    }

    private Action PrepareIncoming(IncomingEvent item)
    {
        try
        {
            CapturedEventFacts facts = item.Facts.Interpolation?.Render(item.Facts) ?? item.Facts;
            ProcessingDecision? decision = processing?.Prepare(facts, item.Level,
                outputs.Where(output => Interested(output, item.Level)).Select(output => output.Name).ToArray());
            if (decision is { Suppressed: true })
            {
                return () =>
                {
                    incoming!.RecordSuppressed(decision.Duplicate);
                    foreach (var output in outputs) { output.Admission.RecordFiltered(); }
                };
            }
            Output[] preparing = CapturePreparingOutputs(item.Level);
            if (decision is not null) { preparing = preparing.Where(output => decision.Destinations.Contains(output.Name, StringComparer.Ordinal)).ToArray(); }
            Func<LogWriteResult> commit = PrepareCapturedEvent(decision?.Facts ?? facts, item.Level, preparing, fromIncoming: true, decision?.Destinations, item.Facts.IsTruncated);
            return () =>
            {
                LogWriteResult result = commit();
                if (result.Outcome is LogWriteOutcome.Rejected or LogWriteOutcome.PartiallyAccepted)
                { incoming!.RecordWriterRejection(); }
                if ((result.Outcome is LogWriteOutcome.Accepted or LogWriteOutcome.PartiallyAccepted) && processing?.Remember(decision?.Key) == true)
                { incoming!.RecordDuplicateKeyEviction(); }
            };
        }
        catch (EventEnvelopeTooLargeException)
        {
            // Commit this disposition under the incoming gate; abandoned preparation adds no loss.
            return () =>
            {
                invalidEventCount.Increment();
                workerInvalid.Increment();
                throw new EventEnvelopeTooLargeException();
            };
        }
    }

    private PipelineBoundary RegisterIncomingBoundary(long started, bool final = false)
    {
        lock (admissionGate)
        {
            return new(DateTimeOffset.UtcNow, InvalidEventCount, TruncatedEventCount, workerInvalid.Value, workerTruncated.Value,
                incoming!.Capture().QueueFull,
                outputs.ToDictionary(output => output.Name,
                    output => new LogOutputRejectionSnapshot(output.CallerFiltered, 0, output.CallerUnavailable)),
                incoming.RegisterBoundary(started, final), final);
        }
    }

    private async Task WaitPipelineBoundary(PipelineBoundary boundary, CancellationToken token)
    {
        await boundary.Incoming.Ready.Task.WaitAsync(token).ConfigureAwait(false);
        IncomingBoundaryObservation observation = incoming!.Observe(boundary.Incoming);
        if (observation.Outputs is not { } downstream) { return; }
        await downstream.WaitForWriterFlushAsync(token).ConfigureAwait(false);
        foreach (var entry in downstream.Entries)
        { await queuedOutputs!.Get(entry.Name).WaitForCoveredRecordsAsync(entry.Name, entry.Boundary, token).ConfigureAwait(false); }
    }

    private LogFlushResult CapturePipelineResult(PipelineBoundary boundary, ElapsedBudget budget)
    {
        lock (admissionGate)
        {
            IncomingBoundaryObservation frontend = incoming!.Observe(boundary.Incoming);
            RuntimeFlushBoundary? downstream = frontend.Outputs;
            var entries = downstream?.Entries.ToArray() ?? [];
            var observations = entries.Select(entry =>
            {
                LogOutputRejectionSnapshot caller = boundary.Rejections[entry.Name];
                return queuedOutputs!.Get(entry.Name).CaptureDrain(entry.Name, entry.Boundary with
                {
                    Filtered = AddLoss(entry.Boundary.Filtered, caller.Filtered),
                    Unavailable = AddLoss(entry.Boundary.Unavailable, caller.Unavailable)
                });
            });
            bool late = frontend.SettledAt is not { } settled || !budget.Contains(settled) ||
                entries.Any(entry => entry.Boundary.Registration.SettledAt is not { } timestamp || !budget.Contains(timestamp));
            return new LogFlushResult(boundary.CapturedAtUtc, DateTimeOffset.UtcNow,
                AddLoss(boundary.Invalid, Math.Max(0, (downstream?.InvalidEventCount ?? workerInvalid.Value) - boundary.WorkerInvalid)),
                AddLoss(boundary.Truncated, Math.Max(0, (downstream?.TruncatedEventCount ?? workerTruncated.Value) - boundary.WorkerTruncated)),
                observations, late, new LogIncomingSnapshot(frontend.Counts),
                registrationLoss: boundary.Rejections.Values.Any(rejection => rejection.Unavailable > 0),
                frontendBatch: frontend.Batch is { } batch ? new(batch.TargetCount, batch.Consumed, batch.Evicted, batch.Abandoned, batch.Pending) : null,
                requireFrontendBatch: !boundary.Final,
                completion: frontend.Failure is null ? null : LogCompletion.Failed, errorCode: frontend.Failure,
                // The final ready signal follows publisher-zero and the final empty pass.
                // A late failed enrollment recheck cannot invalidate that completed drain.
                frontendDrainComplete: boundary.Final && boundary.Incoming.Ready.Task.IsCompletedSuccessfully);
        }
    }

    private async Task<LogFlushResult> FlushIncomingAsync(TimeSpan timeout, long started, CancellationToken caller)
    {
        PipelineBoundary? boundary = null;
        var budget = new ElapsedBudget(started, timeout, timestampFrequency);
        try
        {
            boundary = RegisterIncomingBoundary(started);
            await using var deadline = new FlushDeadline(timeProvider, started, timeout, () => CapturePipelineResult(boundary, budget));
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(caller, deadline.Token);
            try { await WaitPipelineBoundary(boundary, wait.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (wait.IsCancellationRequested) { }
            if (deadline.Token.IsCancellationRequested) { return deadline.ObserveCompletion(); }
            if (caller.IsCancellationRequested)
            { return CapturePipelineResult(boundary, budget).WithCompletion(LogCompletion.Canceled, "FlushCanceled"); }
            return deadline.ObserveCompletion();
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException))
        {
            if (boundary is not null)
            { return CapturePipelineResult(boundary, budget).WithCompletion(LogCompletion.Failed, "FlushFailed"); }
            return FrontendFailure("FlushFailed");
        }
        finally { if (boundary is not null) { incoming!.Unregister(boundary.Incoming); } }
    }

    private LogFlushResult FrontendFailure(string code, LogCompletion completion = LogCompletion.Failed) =>
        new(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, InvalidEventCount, TruncatedEventCount, [],
            incoming: incoming is null ? null : new(incoming.Capture()), completion: completion, errorCode: code);

    private Task<LogStopResult> StopIncomingAsync(CancellationToken caller)
    {
        Task<LogStopResult>? shared;
        lock (admissionGate) { shared = stopTask; }
        if (shared is not null) { return ObserveIncomingStopAsync(shared, caller); }
        long started;
        try { started = timeProvider.GetTimestamp(); }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException))
        { return ObserveIncomingStopAsync(AbortIncomingStop("StopClockFailed"), caller); }

        PipelineBoundary? boundary = null;
        TaskCompletionSource<LogStopResult>? completion = null;
        var budget = new ElapsedBudget(started, shutdownTimeout, timestampFrequency);
        lock (admissionGate)
        {
            if (stopTask is null)
            {
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                stopTask = completion.Task;
                try
                {
                    boundary = RegisterIncomingBoundary(started, final: true);
                    shutdownStartedAtUtc ??= boundary.CapturedAtUtc;
                    admissionClosed = true;
                    queuedOutputs!.SetStopBudget(budget);
                    incoming!.Complete(budget, queuedOutputs.Complete);
                }
                catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException))
                {
                    admissionClosed = true;
                    incoming!.Abandon(started);
                    incoming.Complete(new(started, TimeSpan.Zero, timestampFrequency), queuedOutputs!.Complete);
                    if (boundary is not null) { incoming.Unregister(boundary.Incoming); }
                    var failed = new LogStopResult(FrontendFailure("StopFailed"));
                    lastStopResult = failed; completion.TrySetResult(failed); completion = null;
                }
            }
            shared = stopTask;
        }
        if (completion is not null)
        {
            if (ExecutionContext.IsFlowSuppressed()) { _ = RunIncomingStopAsync(boundary!, budget, completion); }
            else { using (ExecutionContext.SuppressFlow()) { _ = RunIncomingStopAsync(boundary!, budget, completion); } }
        }
        return ObserveIncomingStopAsync(shared!, caller);
    }

    private Task<LogStopResult> AbortIncomingStop(string code)
    {
        lock (admissionGate)
        {
            if (stopTask is not null) { return stopTask; }
            admissionClosed = true; shutdownStartedAtUtc ??= DateTimeOffset.UtcNow;
            incoming!.Abandon(0);
            incoming.Complete(new(0, TimeSpan.Zero, timestampFrequency), queuedOutputs!.Complete);
            lastStopResult = new LogStopResult(FrontendFailure(code));
            return stopTask = Task.FromResult(lastStopResult);
        }
    }

    private Task<LogStopResult> ObserveIncomingStopAsync(Task<LogStopResult> shared, CancellationToken caller) =>
        caller.CanBeCanceled ? ObserveCancelableIncomingStopAsync(shared, caller) : shared;

    private async Task<LogStopResult> ObserveCancelableIncomingStopAsync(Task<LogStopResult> shared, CancellationToken caller)
    {
        try
        {
            if (caller.IsCancellationRequested) { return new(FrontendFailure("StopCanceled", LogCompletion.Canceled)); }
            return await shared.WaitAsync(caller).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (caller.IsCancellationRequested)
        { return new(FrontendFailure("StopCanceled", LogCompletion.Canceled)); }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException))
        { return new(FrontendFailure("StopFailed")); }
    }

    private async Task RunIncomingStopAsync(PipelineBoundary boundary, ElapsedBudget budget, TaskCompletionSource<LogStopResult> completion)
    {
        try
        {
            await using var deadline = new FlushDeadline(timeProvider, budget.Started, budget.Timeout,
                () => CapturePipelineResult(boundary, budget), () => CaptureIncomingDeadline(boundary, budget));
            try
            {
                await WaitPipelineBoundary(boundary, deadline.Token).ConfigureAwait(false);
                try { await Completion.WaitAsync(deadline.Token).ConfigureAwait(false); }
                catch (Exception) when ((Completion.IsFaulted || Completion.IsCanceled) && queuedOutputs!.Entries.Any(entry => entry.Queue.IsFaulted))
                { /* Writer/cleanup faults remain represented in output observations. */ }
            }
            catch (OperationCanceledException) when (deadline.Token.IsCancellationRequested) { }
            PublishStop(deadline.ObserveCompletion(), completion);
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException))
        {
            if (completion.Task.IsCompletedSuccessfully) { queuedOutputs!.RecordCleanupFault("ShutdownTimerCleanupFailed"); }
            else
            {
                incoming!.Abandon(budget.Started);
                PublishStop(FrontendFailure("StopFailed"), completion);
            }
        }
        finally { incoming!.Unregister(boundary.Incoming); }
    }

    private LogFlushResult CaptureIncomingDeadline(PipelineBoundary boundary, ElapsedBudget budget)
    {
        long observedAt = timeProvider.GetTimestamp();
        LogFlushResult observation;
        lock (admissionGate)
        {
            incoming!.Abandon(observedAt);
            queuedOutputs!.AbandonUnstarted(observedAt);
            observation = CapturePipelineResult(boundary, budget);
        }
        queuedOutputs.RequestIoCancellation();
        return observation;
    }

    private static long AddLoss(long initial, long additional) => initial > long.MaxValue - additional ? long.MaxValue : initial + additional;

    private sealed record PipelineBoundary(DateTimeOffset CapturedAtUtc, long Invalid, long Truncated,
        long WorkerInvalid, long WorkerTruncated, long QueueFull,
        IReadOnlyDictionary<string, LogOutputRejectionSnapshot> Rejections, IncomingFlushBoundary Incoming, bool Final);
}

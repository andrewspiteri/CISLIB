using CIS.Logging.Events;
using CIS.Logging.Runtime;

namespace CIS.Logging;

public sealed partial class CoreLogRuntime
{
    private IncomingEventQueue? incoming;
    private Task? pipelineCompletion;

    // Invoked exactly once by unpublished factory composition, after output initialization succeeds.
    internal void StartIncoming(int capacity, Func<Func<Task>, Task>? launch = null)
    {
        if (incoming is not null || pipelineCompletion is not null) { throw new InvalidOperationException("Runtime startup is sealed."); }
        incoming = new IncomingEventQueue(capacity, admissionGate, timeProvider, PrepareIncoming, RegisterFlushBoundaryAt, launch);
    }

    private RuntimeFlushBoundary RegisterFlushBoundaryAt(long timestamp) => RegisterFlushBoundary(timestamp);

    private Action PrepareIncoming(IncomingEvent item)
    {
        try
        {
            Func<LogWriteResult> commit = PrepareCapturedEvent(item.Facts, item.Level, CapturePreparingOutputs(item.Level), fromIncoming: true);
            return () =>
            {
                LogWriteResult result = commit();
                if (result.Outcome is LogWriteOutcome.Rejected or LogWriteOutcome.PartiallyAccepted)
                { incoming!.RecordWriterRejection(); }
            };
        }
        catch (EventEnvelopeTooLargeException)
        {
            IncrementLossCounter(ref invalidEventCount);
            throw;
        }
    }

    private PipelineBoundary RegisterIncomingBoundary(long started)
    {
        lock (admissionGate)
        {
            return new(DateTimeOffset.UtcNow, InvalidEventCount, TruncatedEventCount, incoming!.RegisterBoundary(started));
        }
    }

    private async Task WaitPipelineBoundary(PipelineBoundary boundary, CancellationToken token)
    {
        await boundary.Incoming.Ready.Task.WaitAsync(token).ConfigureAwait(false);
        RuntimeFlushBoundary downstream;
        lock (admissionGate) { downstream = boundary.Incoming.Outputs!.Value; }
        await downstream.WaitForWriterFlushAsync(token).ConfigureAwait(false);
        foreach (var entry in downstream.Entries)
        { await queuedOutputs!.Get(entry.Name).WaitForCoveredRecordsAsync(entry.Name, entry.Boundary, token).ConfigureAwait(false); }
    }

    private LogFlushResult CapturePipelineResult(PipelineBoundary boundary, ElapsedBudget budget)
    {
        lock (admissionGate)
        {
            RuntimeFlushBoundary? downstream = boundary.Incoming.Outputs;
            var entries = downstream?.Entries.ToArray() ?? [];
            var observations = entries.Select(entry => queuedOutputs!.Get(entry.Name).CaptureDrain(entry.Name, entry.Boundary));
            bool late = boundary.Incoming.SettledAt is not { } settled || !budget.Contains(settled) ||
                entries.Any(entry => entry.Boundary.Registration.SettledAt is not { } timestamp || !budget.Contains(timestamp));
            return new LogFlushResult(boundary.CapturedAtUtc, DateTimeOffset.UtcNow,
                downstream?.InvalidEventCount ?? boundary.Invalid, downstream?.TruncatedEventCount ?? boundary.Truncated,
                observations, late, new LogIncomingSnapshot(incoming!.Capture(boundary.Incoming)));
        }
    }

    private async Task<LogFlushResult> FlushIncomingAsync(TimeSpan timeout, long started, CancellationToken caller)
    {
        PipelineBoundary boundary = RegisterIncomingBoundary(started);
        caller.ThrowIfCancellationRequested();
        var budget = new ElapsedBudget(started, timeout, timestampFrequency);
        await using var deadline = new FlushDeadline(timeProvider, started, timeout, () => CapturePipelineResult(boundary, budget));
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(caller, deadline.Token);
        try { await WaitPipelineBoundary(boundary, wait.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (wait.IsCancellationRequested) { caller.ThrowIfCancellationRequested(); }
        caller.ThrowIfCancellationRequested();
        return deadline.ObserveCompletion();
    }

    private Task<LogStopResult> StopIncomingAsync(CancellationToken caller)
    {
        Task<LogStopResult>? shared;
        lock (admissionGate) { shared = stopTask; }
        if (shared is null)
        {
            long started = timeProvider.GetTimestamp();
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
                        boundary = RegisterIncomingBoundary(started);
                        shutdownStartedAtUtc ??= boundary.CapturedAtUtc;
                        admissionClosed = true;
                        queuedOutputs!.SetStopBudget(budget);
                        // Output queues close only after all incoming work has been dispatched or discarded.
                        incoming!.Complete(budget, queuedOutputs.Complete);
                    }
                    catch (Exception error) { completion.TrySetException(error); throw; }
                }
                shared = stopTask;
            }
            if (completion is not null)
            {
                if (ExecutionContext.IsFlowSuppressed()) { _ = RunIncomingStopAsync(boundary!, budget, completion); }
                else { using (ExecutionContext.SuppressFlow()) { _ = RunIncomingStopAsync(boundary!, budget, completion); } }
            }
        }
        if (caller.IsCancellationRequested) { return Task.FromCanceled<LogStopResult>(caller); }
        return caller.CanBeCanceled ? shared!.WaitAsync(caller) : shared!;
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
        catch (Exception error)
        {
            if (completion.Task.IsCompletedSuccessfully) { queuedOutputs!.RecordCleanupFault("ShutdownTimerCleanupFailed"); }
            else { completion.TrySetException(error); }
        }
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

    private sealed record PipelineBoundary(DateTimeOffset CapturedAtUtc, long Invalid, long Truncated, IncomingFlushBoundary Incoming);
}

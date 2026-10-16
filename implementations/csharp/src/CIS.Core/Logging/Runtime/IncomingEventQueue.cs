using CIS.Logging.Events;

namespace CIS.Logging.Runtime;

// Capture is already owned. Preparation runs outside the gate; commit is a trusted, nonblocking
// output-admission action. This split lets expiry discard preparation without racing a later fan-out.
internal sealed class IncomingEventQueue
{
    private readonly object gate;
    private readonly int capacity;
    private readonly TimeProvider clock;
    private readonly Func<IncomingEvent, Action> prepare;
    private readonly Func<long, RuntimeFlushBoundary> captureOutputs;
    private readonly Queue<IncomingEvent> pending = new();
    private readonly SortedDictionary<long, IncomingFlushBoundary> boundaries = new();
    private TaskCompletionSource wake = NewSignal();
    private long accepted, settled, processed, failed, queueFull, rejectedByWriters;
    private long suppressed, duplicateSuppressed, duplicateKeyEvictions;
    private long firstAbandoned;
    private bool closed, abandoned, drained;
    private ElapsedBudget? stopBudget;
    private Action? onDrained;

    internal IncomingEventQueue(int capacity, object gate, TimeProvider clock,
        Func<IncomingEvent, Action> prepare, Func<long, RuntimeFlushBoundary> captureOutputs,
        Func<Func<Task>, Task>? launch = null)
    {
        if (capacity is < LoggingOptions.MinimumQueueCapacity or > LoggingOptions.MaximumQueueCapacity)
        { throw new ArgumentOutOfRangeException(nameof(capacity)); }
        this.capacity = capacity; this.gate = gate; this.clock = clock;
        this.prepare = prepare; this.captureOutputs = captureOutputs;
        Task Start() => (launch is null ? Task.Run(ProcessAsync) : launch(ProcessAsync))
            ?? throw new InvalidOperationException("Worker launch must return its scheduled task.");
        if (ExecutionContext.IsFlowSuppressed()) { Completion = Start(); }
        else { using (ExecutionContext.SuppressFlow()) { Completion = Start(); } }
    }

    internal Task Completion { get; }
    // Called only by the trusted commit action under this queue's gate; a subset of Processed.
    internal void RecordWriterRejection() { rejectedByWriters++; }
    internal void RecordSuppressed(bool duplicate) { suppressed++; if (duplicate) { duplicateSuppressed++; } }
    internal void RecordDuplicateKeyEviction() { duplicateKeyEvictions++; }
    internal long? TryAdmit(CoreLogLevel level, CapturedEventFacts facts)
    {
        lock (gate)
        {
            if (closed || accepted == long.MaxValue) { return null; }
            if (pending.Count == capacity)
            {
                if (queueFull != long.MaxValue) { queueFull++; }
                return null;
            }
            long sequence = ++accepted;
            pending.Enqueue(new(sequence, level, facts)); wake.TrySetResult();
            return sequence;
        }
    }

    internal IncomingFlushBoundary RegisterBoundary(long observedAt)
    {
        lock (gate)
        {
            if (boundaries.TryGetValue(accepted, out var existing)) { return existing; }
            var boundary = new IncomingFlushBoundary(accepted);
            if (settled == accepted) { Resolve(boundary, observedAt); }
            else { boundaries.Add(accepted, boundary); }
            return boundary;
        }
    }

    internal IncomingAccounting Capture(IncomingFlushBoundary? boundary = null, long? callerQueueFull = null)
    {
        lock (gate)
        {
            if (boundary?.Observation is { } frozen) { return frozen with { QueueFull = callerQueueFull ?? frozen.QueueFull }; }
            long target = boundary?.Target ?? accepted;
            long discarded = firstAbandoned == 0 ? 0 : Math.Max(0, target - firstAbandoned + 1);
            return new(target, processed, failed, discarded, target - processed - failed - discarded,
                callerQueueFull ?? queueFull, rejectedByWriters, suppressed, duplicateSuppressed, duplicateKeyEvictions);
        }
    }

    internal void Complete(ElapsedBudget budget, Action afterDrain)
    {
        lock (gate)
        {
            closed = true; stopBudget = budget; onDrained = afterDrain;
            FinishIfDrained(); wake.TrySetResult();
        }
    }

    internal void Abandon(long observedAt)
    {
        lock (gate) { AbandonCore(observedAt); }
    }

    private void AbandonCore(long observedAt)
    {
        if (!abandoned)
        {
            abandoned = true; closed = true;
            if (settled < accepted) { firstAbandoned = settled + 1; }
            pending.Clear(); settled = accepted;
            ResolveReady(observedAt); FinishIfDrained(); wake.TrySetResult();
        }
    }

    private void Resolve(IncomingFlushBoundary boundary, long observedAt)
    {
        boundary.Observation = Capture(boundary);
        boundary.Outputs = captureOutputs(observedAt);
        boundary.SettledAt = observedAt;
        boundary.Ready.TrySetResult();
    }

    private void ResolveReady(long observedAt)
    {
        // Controls are never capacity-limited or dropped. Resolve before processing later records.
        foreach (long target in boundaries.Keys.TakeWhile(target => target <= settled).ToArray())
        {
            Resolve(boundaries[target], observedAt); boundaries.Remove(target);
        }
    }

    private void FinishIfDrained()
    {
        if (closed && settled == accepted && !drained && onDrained is not null)
        { drained = true; onDrained?.Invoke(); }
    }

    private async Task ProcessAsync()
    {
        while (true)
        {
            IncomingEvent? next = null; Task? wait = null;
            long observedAt = clock.GetTimestamp();
            lock (gate)
            {
                if (stopBudget is { } budget && !budget.Contains(observedAt)) { AbandonCore(observedAt); }
                if (closed && settled == accepted) { FinishIfDrained(); return; }
                if (pending.TryDequeue(out var item)) { next = item; }
                else
                {
                    if (wake.Task.IsCompleted) { wake = NewSignal(); }
                    wait = wake.Task;
                }
            }
            if (wait is not null) { await wait.ConfigureAwait(false); continue; }

            Action? commit = null;
            try { commit = prepare(next!.Value); }
            catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException))
            { /* The owned event failed background preparation; subsequent events can still progress. */ }
            observedAt = clock.GetTimestamp();
            lock (gate)
            {
                if (stopBudget is { } budget && !budget.Contains(observedAt)) { AbandonCore(observedAt); }
                if (abandoned) { return; } // No output admission after expiry, even if preparation finished late.
                bool succeeded = false;
                try { if (commit is not null) { commit(); succeeded = true; } }
                catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException)) { }
                if (succeeded) { processed++; } else { failed++; }
                settled = next!.Value.Sequence;
                ResolveReady(observedAt); FinishIfDrained();
            }
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal readonly record struct IncomingEvent(long Sequence, CoreLogLevel Level, CapturedEventFacts Facts);
internal readonly record struct IncomingAccounting(long Accepted, long Processed, long Failed, long Abandoned, long Pending, long QueueFull, long RejectedByWriters = 0,
    long Suppressed = 0, long DuplicateSuppressed = 0, long DuplicateKeyEvictions = 0);
internal sealed class IncomingFlushBoundary(long target)
{
    internal long Target { get; } = target;
    internal TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal IncomingAccounting? Observation { get; set; }
    internal RuntimeFlushBoundary? Outputs { get; set; }
    internal long? SettledAt { get; set; }
}

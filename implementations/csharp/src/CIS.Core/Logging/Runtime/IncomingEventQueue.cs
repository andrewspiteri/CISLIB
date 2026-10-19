using CIS.Logging.Events;

namespace CIS.Logging.Runtime;

// The private gate coordinates controls and consumer metadata only. Publication never acquires it.
internal sealed class IncomingEventQueue
{
    private readonly object gate = new();
    private readonly IncomingRingBuffer ring;
    private readonly IncomingBatchIndex index;
    private readonly TimeProvider clock;
    private readonly TimeSpan pollInterval;
    private readonly Func<IncomingEvent, Action> prepare;
    private readonly Func<long, RuntimeFlushBoundary> captureOutputs;
    private readonly AtomicLogCounter processed = new(), failed = new(), rejectedByWriters = new();
    private readonly AtomicLogCounter suppressed = new(), duplicateSuppressed = new(), duplicateKeyEvictions = new();
    private IncomingFlushBoundary? pending, current, stopping;
    private ElapsedBudget? stopBudget;
    private Action? onDrained;
    private long tickets;
    private bool abandonRequested, drained, backendActive;
    private string? failure;
    private TaskCompletionSource controlWake = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal IncomingEventQueue(int capacity, object gate, TimeProvider clock,
        Func<IncomingEvent, Action> prepare, Func<long, RuntimeFlushBoundary> captureOutputs,
        Func<Func<Task>, Task>? launch = null, LoggingOptions? options = null)
    {
        // Retain the existing composition signature; the backend's gate is deliberately not shared.
        _ = gate;
        options ??= new LoggingOptions();
        ring = new(capacity, options); index = new(capacity);
        pollInterval = TimeSpan.FromMilliseconds(options.IncomingPollIntervalMilliseconds);
        this.clock = clock; this.prepare = prepare; this.captureOutputs = captureOutputs;
        Task Start() => (launch is null ? Task.Run(ProcessAsync) : launch(ProcessAsync))
            ?? throw new InvalidOperationException("Worker launch must return its scheduled task.");
        if (ExecutionContext.IsFlowSuppressed()) { Completion = Start(); }
        else { using (ExecutionContext.SuppressFlow()) { Completion = Start(); } }
    }

    internal Task Completion { get; }
    internal bool IsClosed => ring.IsClosed;
    internal void RecordWriterRejection() => rejectedByWriters.Increment();
    internal void RecordSuppressed(bool duplicate)
    { suppressed.Increment(); if (duplicate) { duplicateSuppressed.Increment(); } }
    internal void RecordDuplicateKeyEviction() => duplicateKeyEvictions.Increment();

    internal long? TryAdmit(CoreLogLevel level, CapturedEventFacts facts)
    {
        if (facts is null || level is < CoreLogLevel.Trace or > CoreLogLevel.Critical || ring.IsClosed) { return null; }
        long ordinal = Interlocked.Increment(ref tickets);
        long ticket = (long)(unchecked((ulong)(ordinal - 1)) % long.MaxValue) + 1;
        return ring.TryPublish(new(ticket, level, facts), out _) == IncomingAdmission.Accepted ? ticket : null;
    }

    internal IncomingFlushBoundary RegisterBoundary(long observedAt, bool final = false)
    {
        _ = observedAt; // Caller budgets are independent of capture-pass membership.
        IncomingFlushBoundary result;
        bool resolve;
        lock (gate)
        {
            resolve = drained;
            if (final) { result = stopping ??= new(); }
            else { result = pending ??= new(); }
            result.Waiters++;
            if (resolve && ReferenceEquals(pending, result)) { pending = null; }
            controlWake.TrySetResult();
        }
        if (resolve)
        {
            lock (gate) { result.Batch = new(0, 0, 0, 0, 0); }
            Resolve(result);
        }
        return result;
    }

    internal void Unregister(IncomingFlushBoundary boundary)
    {
        lock (gate)
        {
            if (boundary.Waiters > 0) { boundary.Waiters--; }
            if (boundary.Waiters == 0 && ReferenceEquals(pending, boundary)) { pending = null; }
        }
    }

    internal IncomingAccounting Capture(IncomingFlushBoundary? boundary = null)
    {
        lock (gate)
        {
            if (boundary?.Observation is { } frozen) { return frozen; }
            IncomingRingSnapshot counts = ring.Capture();
            return new(counts.Admitted, processed.Value, failed.Value, counts.Abandoned,
                counts.Waiting + (backendActive ? 1 : 0), 0, rejectedByWriters.Value, suppressed.Value,
                duplicateSuppressed.Value, duplicateKeyEvictions.Value, counts.Consumed, counts.Evicted,
                counts.Waiting, counts.ActivePublishers, counts.IsSaturated, drained && !counts.IsSaturated, failure);
        }
    }

    internal IncomingBatchAccounting? CaptureBatch(IncomingFlushBoundary boundary)
    { lock (gate) { return boundary.Batch; } }

    internal IncomingBoundaryObservation Observe(IncomingFlushBoundary boundary)
    {
        lock (gate) { return new(boundary.Observation ?? Capture(), boundary.Batch, boundary.Outputs, boundary.SettledAt, boundary.Failure); }
    }

    internal void Complete(ElapsedBudget budget, Action afterDrain)
    {
        bool alreadyDrained;
        lock (gate)
        {
            alreadyDrained = drained && onDrained is null;
            stopBudget ??= budget;
            onDrained ??= afterDrain;
            ring.Close();
            controlWake.TrySetResult();
        }
        if (alreadyDrained) { afterDrain(); }
    }

    internal void Abandon(long observedAt)
    {
        _ = observedAt;
        lock (gate) { ring.Close(); abandonRequested = true; controlWake.TrySetResult(); }
    }

    // Called under gate before inspecting controls, so a later signal cannot be lost before waiting.
    private Task CaptureControlWake()
    {
        if (controlWake.Task.IsCompleted) { controlWake = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        return controlWake.Task;
    }

    private async Task WaitForPollAsync(Task wake)
    {
        // Configured idle polling must not delay closed-state cleanup of enrolled publishers.
        TimeSpan delay = ring.IsClosed ? TimeSpan.FromMilliseconds(1) : pollInterval;
        try { await wake.WaitAsync(delay).ConfigureAwait(false); }
        catch (TimeoutException) { }
    }

    private void Resolve(IncomingFlushBoundary boundary)
    {
        RuntimeFlushBoundary? outputs = null;
        long observedAt = 0;
        string? error = null;
        try
        {
            bool needed;
            lock (gate) { needed = boundary.Waiters > 0 && failure is null; }
            if (needed) { observedAt = clock.GetTimestamp(); outputs = captureOutputs(observedAt); observedAt = clock.GetTimestamp(); }
        }
        catch (Exception problem) when (problem is not (OutOfMemoryException or StackOverflowException))
        { error = "IncomingFlushFailed"; }
        lock (gate)
        {
            boundary.Observation = Capture();
            boundary.Outputs = outputs; boundary.SettledAt = observedAt; boundary.Failure = error ?? failure;
        }
        boundary.Ready.TrySetResult();
    }

    private async Task ProcessAsync()
    {
        try { await ProcessCoreAsync().ConfigureAwait(false); }
        catch (Exception problem) when (problem is not (OutOfMemoryException or StackOverflowException))
        {
            // The sole cleanup owner stays alive for enrolled publishers, including after a worker fault.
            lock (gate) { ring.Close(); failure = "IncomingWorkerFailed"; abandonRequested = true; }
            for (int position = 0; position < index.Count; position++)
            {
                IncomingBatchIndex.Entry entry = index.At(position);
                if (entry.Item is not { } item) { continue; }
                lock (gate)
                {
                    bool removed = ring.TryAbandon(entry.Slot, item);
                    if (current?.Batch is { } counts)
                    {
                        current.Batch = counts with
                        {
                            Abandoned = counts.Abandoned + (removed ? 1 : 0),
                            Evicted = counts.Evicted + (removed ? 0 : 1),
                            Pending = counts.Pending - 1
                        };
                    }
                }
                index.Clear(position);
            }
            do
            {
                Task wake;
                lock (gate) { wake = CaptureControlWake(); }
                for (int slot = 0; slot < ring.Capacity; slot++) { ring.AbandonSlot(slot); }
                if (ring.ActivePublishers == 0) { break; }
                await WaitForPollAsync(wake).ConfigureAwait(false);
            } while (true);
            for (int slot = 0; slot < ring.Capacity; slot++) { ring.AbandonSlot(slot); }
            index.Release();
            IncomingFlushBoundary?[] controls;
            Action? callback;
            lock (gate)
            {
                controls = [current, pending, stopping]; current = pending = stopping = null;
                drained = true; backendActive = false; callback = onDrained;
            }
            foreach (var control in controls.Where(value => value is not null).Distinct()) { Resolve(control!); }
            try { callback?.Invoke(); }
            catch (Exception cleanup) when (cleanup is not (OutOfMemoryException or StackOverflowException)) { }
        }
    }

    private async Task ProcessCoreAsync()
    {
        while (true)
        {
            bool finalPass;
            Task wake;
            IncomingFlushBoundary? batch;
            lock (gate)
            {
                wake = CaptureControlWake();
                finalPass = ring.IsClosed && ring.ActivePublishers == 0;
                batch = current = pending; pending = null;
            }
            // Group freeze precedes construction; new requests cannot join a partly built capture.
            index.Capture(ring);
            int count = index.Count;
            lock (gate) { if (batch is not null) { batch.Batch = new(count, 0, 0, 0, count); } }
            for (int position = 0; position < count; position++)
            {
                IncomingBatchIndex.Entry entry = index.At(position);
                IncomingEvent item = entry.Item!;
                bool claimed;
                long observedAt = clock.GetTimestamp();
                lock (gate)
                {
                    if (stopBudget is { } budget && !budget.Contains(observedAt)) { abandonRequested = true; }
                    bool abandoned = abandonRequested && ring.TryAbandon(entry.Slot, item);
                    claimed = !abandonRequested && ring.TryClaim(entry.Slot, item);
                    if (claimed) { backendActive = true; }
                    if (batch?.Batch is { } counts)
                    {
                        batch.Batch = counts with
                        {
                            Consumed = counts.Consumed + (claimed ? 1 : 0),
                            Abandoned = counts.Abandoned + (abandoned ? 1 : 0),
                            Evicted = counts.Evicted + (!claimed && !abandoned ? 1 : 0),
                            Pending = counts.Pending - 1
                        };
                    }
                }
                index.Clear(position);
                if (!claimed) { continue; }
                bool success = false;
                try { Action commit = prepare(item); commit(); success = true; }
                catch (Exception problem) when (problem is not (OutOfMemoryException or StackOverflowException)) { }
                lock (gate)
                {
                    if (success) { processed.Increment(); } else { failed.Increment(); }
                    backendActive = false;
                }
            }
            index.Release();
            if (batch is not null) { Resolve(batch); }
            lock (gate) { current = null; }
            if (finalPass && count == 0)
            {
                IncomingFlushBoundary? terminal, lastPending;
                Action? callback;
                lock (gate)
                {
                    drained = true;
                    terminal = stopping; stopping = null; lastPending = pending; pending = null; callback = onDrained;
                    if (lastPending is not null) { lastPending.Batch = new(0, 0, 0, 0, 0); }
                }
                // Register final writer flushes before allowing idle output workers to exit.
                if (lastPending is not null) { Resolve(lastPending); }
                if (terminal is not null) { Resolve(terminal); }
                callback?.Invoke();
                return;
            }
            if (count == 0) { await WaitForPollAsync(wake).ConfigureAwait(false); }
        }
    }
}

internal readonly record struct IncomingAccounting(long Accepted, long Processed, long Failed, long Abandoned, long Pending,
    long QueueFull, long RejectedByWriters = 0, long Suppressed = 0, long DuplicateSuppressed = 0, long DuplicateKeyEvictions = 0,
    long Consumed = 0, long Evicted = 0, long Waiting = 0, long ActivePublishers = 0, bool IsSaturated = false, bool IsExact = false, string? Failure = null);
internal readonly record struct IncomingBatchAccounting(long TargetCount, long Consumed, long Evicted, long Abandoned, long Pending);
internal readonly record struct IncomingBoundaryObservation(IncomingAccounting Counts, IncomingBatchAccounting? Batch,
    RuntimeFlushBoundary? Outputs, long? SettledAt, string? Failure);
internal sealed class IncomingFlushBoundary
{
    internal int Waiters { get; set; }
    internal IncomingBatchAccounting? Batch { get; set; }
    internal TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal IncomingAccounting? Observation { get; set; }
    internal RuntimeFlushBoundary? Outputs { get; set; }
    internal long? SettledAt { get; set; }
    internal string? Failure { get; set; }
}

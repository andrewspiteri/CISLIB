using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using CIS.Logging.Events;

namespace CIS.Logging.Runtime;
// One queue and one awaited writer operation. Only trusted internal composition supplies the writer.
internal sealed class OutputEventQueue : ILogOutputAdmission
{
    private readonly object admissionGate;
    private readonly TimeProvider clock;
    private ElapsedBudget? stopBudget;
    private readonly Channel<AdmittedEvent> channel;
    private readonly Func<AdmittedEvent, CancellationToken, ValueTask> write;
    private readonly CancellationTokenSource? ioCancellation;
    private Task? ioCancellationCompletion;
    private bool ioCancellationClosed;
    private readonly IDisposable? ownedResource;
    private readonly Func<ValueTask>? cleanup;
    private readonly Func<CancellationToken, ValueTask>? flush;
    private readonly SortedDictionary<long, OutputFlushRegistration> flushRequests = [];
    private TaskCompletionSource wake = NewWake();
    private OutputFlushRegistration? flushing;
    private ITimer? fileFlushTimer;
    private Task? fileFlushTimerDisposal;
    private bool workerExiting;
    private long lastAcceptedSequence;
    private long acceptedCount;
    private long queueFullCount;
    private long writtenCount;
    private long failedCount;
    private long abandonedCount;
    private bool inFlight;
    private long inFlightSequence;
    private long failedSequence;
    private long firstAbandonedSequence;
    private long lastAbandonedSequence;
    private TaskCompletionSource? dispositionChanged;
    private long filteredCount;
    private long unavailableCount;
    private bool admissionClosed;
    private bool faulted;
    private bool stopRequested;
    private bool stopDeadlineExpired;
    private bool workerFinished;
    private LogOutputState state;
    private string? lastFaultCode;
    private DateTimeOffset? lastFaultAtUtc;
    internal OutputEventQueue(int capacity, Func<AdmittedEvent, ValueTask> write, object? sharedAdmissionGate = null, Func<Func<Task>, Task>? launchWorker = null, IDisposable? ownedResource = null, Func<ValueTask>? flush = null, Func<AdmittedEvent, CancellationToken, ValueTask>? cancellableWrite = null, Func<CancellationToken, ValueTask>? cancellableFlush = null, TimeProvider? timeProvider = null, Func<ValueTask>? cleanup = null)
    {
        ArgumentNullException.ThrowIfNull(write);
        if (capacity is < LoggingOptions.MinimumQueueCapacity or > LoggingOptions.MaximumQueueCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        admissionGate = sharedAdmissionGate ?? new object();
        clock = timeProvider ?? TimeProvider.System;
        this.write = cancellableWrite ?? ((admitted, _) => write(admitted));
        this.ownedResource = ownedResource;
        this.cleanup = cleanup;
        this.flush = cancellableFlush ?? (flush is null ? null : _ => flush());
        if (cancellableWrite is not null || cancellableFlush is not null)
        {
            ioCancellation = new CancellationTokenSource();
        }

        channel = Channel.CreateBounded<AdmittedEvent>(new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false, AllowSynchronousContinuations = false });
        // Captured events already contain their context. A worker must not retain its creator's flow.
        // Trusted composition launchers schedule this actual worker or throw before acquiring a task.
        // Production always uses Task.Run; the internal seam permits deterministic acquisition failure.
        Task Launch() => (launchWorker is null ? Task.Run(ProcessAsync) : launchWorker(ProcessAsync)) ?? throw new InvalidOperationException("Worker launch must return its scheduled task.");
        try
        {
            if (ExecutionContext.IsFlowSuppressed())
            {
                Completion = Launch();
            }
            else
            {
                using (ExecutionContext.SuppressFlow())
                {
                    Completion = Launch();
                }
            }
        }
        catch
        {
            ioCancellation?.Dispose();
            throw;
        }
    }

    private static TaskCompletionSource NewWake() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task Completion { get; }

    internal int RegisteredFlushCount
    {
        get
        {
            lock (admissionGate)
            {
                return flushRequests.Count;
            }
        }
    }

    internal OutputFlushBoundary RegisterFlushBoundary(long? observedAt = null)
    {
        long timestamp = observedAt ?? clock.GetTimestamp();
        lock (admissionGate)
        {
            long boundary = lastAcceptedSequence;
            if (!flushRequests.TryGetValue(boundary, out OutputFlushRegistration? registration))
            {
                registration = new OutputFlushRegistration(boundary);
                if (faulted || workerExiting || workerFinished || flush is null)
                {
                    registration.Resolve(false, timestamp);
                }

                registration.Observe(timestamp, CoveredRecordsSettled(boundary));
                if (registration.SettledAt is null)
                {
                    // One shared registration per live boundary; no per-caller worker state.
                    flushRequests.Add(boundary, registration);
                    wake.TrySetResult();
                }
            }

            return new OutputFlushBoundary(boundary, filteredCount, queueFullCount, unavailableCount, registration);
        }
    }

    // Configured file composition owns one timer; primitive kernel fixtures opt in explicitly.
    // The callback requests existing fixed-boundary work, never performs or awaits output I/O.
    internal void StartFileFlushTimer(TimeProvider clock, TimeSpan interval)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (interval < TimeSpan.FromMilliseconds(LoggingOptions.MinimumFlushIntervalMilliseconds) || interval > TimeSpan.FromMilliseconds(LoggingOptions.MaximumFlushIntervalMilliseconds))
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        ITimer Create() => clock.CreateTimer(static state => ((OutputEventQueue)state!).RequestFileFlush(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan) ?? throw new InvalidOperationException("Timer creation returned null.");
        ITimer acquired;
        if (ExecutionContext.IsFlowSuppressed())
        {
            acquired = Create();
        }
        else
        {
            using (ExecutionContext.SuppressFlow())
            {
                acquired = Create();
            }
        }

        bool assigned = false;
        try
        {
            bool activate;
            lock (admissionGate)
            {
                if (fileFlushTimer is not null)
                {
                    throw new InvalidOperationException("A file flush timer is already owned.");
                }

                activate = !admissionClosed && !workerExiting && !workerFinished && flush is not null;
                if (activate)
                {
                    fileFlushTimer = acquired;
                    assigned = true;
                }
            }

            if (!activate)
            {
                acquired.Dispose();
                return;
            }

            if (!acquired.Change(interval, interval))
            {
                // Terminal cleanup may have detached/disposed this timer before its initial arm.
                lock (admissionGate)
                {
                    if (!ReferenceEquals(fileFlushTimer, acquired))
                    {
                        return;
                    }
                }

                throw new InvalidOperationException("The file flush timer could not be armed.");
            }
        }
        catch
        {
            bool dispose;
            lock (admissionGate)
            {
                dispose = !assigned || ReferenceEquals(fileFlushTimer, acquired);
                if (ReferenceEquals(fileFlushTimer, acquired))
                {
                    fileFlushTimer = null;
                }
            }

            // An assigned/detached timer belongs to the shared disposal task; a local extra still needs cleanup.
            if (dispose)
            {
                acquired.Dispose();
            }

            throw;
        }
    }

    // One cleanup operation per acquired timer. Its gate barrier also respects an outer
    // shared admission lock: custom timer code runs only after closure/fault publication.
    private Task CancelFileFlushScheduling()
    {
        lock (admissionGate)
        {
            if (fileFlushTimerDisposal is not null)
            {
                return fileFlushTimerDisposal;
            }

            ITimer? owned = fileFlushTimer;
            if (owned is null)
            {
                return Task.CompletedTask;
            }

            fileFlushTimer = null;
            async Task DisposeAsync()
            {
                lock (admissionGate)
                {
                }

                await owned.DisposeAsync().ConfigureAwait(false);
            }

            Task Launch() => Task.Run(DisposeAsync);
            if (ExecutionContext.IsFlowSuppressed())
            {
                fileFlushTimerDisposal = Launch();
            }
            else
            {
                using (ExecutionContext.SuppressFlow())
                {
                    fileFlushTimerDisposal = Launch();
                }
            }

            return fileFlushTimerDisposal;
        }
    }

    private void RequestFileFlush()
    {
        long timestamp = clock.GetTimestamp();
        lock (admissionGate)
        {
            if (!admissionClosed && !workerExiting && !workerFinished)
            {
                RegisterFlushBoundary(timestamp);
            }
        }
    }

    internal int BufferedCount
    {
        get
        {
            lock (admissionGate)
            {
                return channel.Reader.Count;
            }
        }
    }

    internal long LastAcceptedSequence
    {
        get
        {
            lock (admissionGate)
            {
                return lastAcceptedSequence;
            }
        }
    }

    internal long FilteredCount
    {
        get
        {
            lock (admissionGate)
            {
                return filteredCount;
            }
        }
    }

    internal long UnavailableCount
    {
        get
        {
            lock (admissionGate)
            {
                return unavailableCount;
            }
        }
    }

    internal OutputAccounting CaptureAccounting()
    {
        lock (admissionGate)
        {
            return new OutputAccounting(filteredCount, acceptedCount, queueFullCount, unavailableCount, writtenCount, failedCount, abandonedCount, channel.Reader.Count + (inFlight ? 1 : 0), inFlight ? 1 : 0, lastAcceptedSequence, state, lastFaultCode, lastFaultAtUtc);
        }
    }

    internal LogOutputDrainResult CaptureDrain(string name, OutputFlushBoundary boundary)
    {
        lock (admissionGate)
        {
            long target = boundary.AcceptedSequenceBoundary;
            // Successful writes are a prefix; terminal failure is one ticket, abandonment one tail.
            long written = Math.Min(writtenCount, target);
            long failed = failedSequence > 0 && failedSequence <= target ? 1 : 0;
            long abandoned = firstAbandonedSequence > 0 && firstAbandonedSequence <= target ? Math.Min(lastAbandonedSequence, target) - firstAbandonedSequence + 1 : 0;
            Task<bool> confirmation = boundary.Registration.WriterFlushCompletion;
            LogWriterFlushStatus status = !confirmation.IsCompletedSuccessfully ? LogWriterFlushStatus.Pending : confirmation.Result ? LogWriterFlushStatus.Succeeded : LogWriterFlushStatus.Failed;
            return new LogOutputDrainResult(name, target, written, failed, abandoned, target - written - failed - abandoned, status, new LogOutputRejectionSnapshot(boundary.Filtered, boundary.QueueFull, boundary.Unavailable));
        }
    }

    internal async Task WaitForCoveredRecordsAsync(string name, OutputFlushBoundary boundary, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            Task changed;
            lock (admissionGate)
            {
                if (CaptureDrain(name, boundary).Pending == 0)
                {
                    return;
                }

                changed = (dispositionChanged ??= NewWake()).Task;
            }

            await changed.WaitAsync(token).ConfigureAwait(false);
        }
    }

    private bool CoveredRecordsSettled(long target)
    {
        long written = Math.Min(writtenCount, target);
        long failed = failedSequence > 0 && failedSequence <= target ? 1 : 0;
        long abandoned = firstAbandonedSequence > 0 && firstAbandonedSequence <= target ? Math.Min(lastAbandonedSequence, target) - firstAbandonedSequence + 1 : 0;
        return target == written + failed + abandoned;
    }

    private void ObserveRegistrations(long timestamp)
    {
        List<long>? completed = null;
        foreach ((long target, OutputFlushRegistration registration) in flushRequests)
        {
            // Terminal covered records form a prefix. Do not copy/scan the entire
            // uncompleted suffix after each write while a large queue drains.
            if (!CoveredRecordsSettled(target)) { break; }
            registration.Observe(timestamp, recordsSettled: true);
            if (registration.SettledAt is not null) { (completed ??= []).Add(target); }
        }
        if (completed is not null)
        {
            foreach (long target in completed) { flushRequests.Remove(target); }
        }
    }
    internal void SetStopBudget(ElapsedBudget budget)
    {
        lock (admissionGate)
        {
            stopBudget ??= budget;
        }
    }

    private void NotifyDispositionChanged()
    {
        dispositionChanged?.TrySetResult();
        dispositionChanged = null;
    }

    public bool IsFaulted
    {
        get
        {
            lock (admissionGate)
            {
                return faulted;
            }
        }
    }

    internal bool TryEnqueue(EncodedEvent captured) => TryAdmit(captured, out _) == LogOutputWriteStatus.Accepted;
    public LogOutputWriteStatus TryAdmit(EncodedEvent captured, out long? acceptedSequence) => TryAdmit(captured, null, out acceptedSequence);
    internal LogOutputWriteStatus TryAdmit(EncodedEvent captured, ReadOnlyMemory<byte>? preparedLine, out long? acceptedSequence, long? observedAt = null)
    {
        ArgumentNullException.ThrowIfNull(captured);
        long timestamp = observedAt ?? clock.GetTimestamp();
        lock (admissionGate)
        {
            acceptedSequence = null;
            if (admissionClosed)
            {
                if (faulted)
                {
                    RecordUnavailable();
                    return LogOutputWriteStatus.Faulted;
                }

                return LogOutputWriteStatus.Stopped;
            }

            if (lastAcceptedSequence == long.MaxValue || acceptedCount == long.MaxValue)
            {
                // No wrap, negative/duplicate ticket or invented acceptance. Exhaustion is terminal.
                Fault("AdmissionExhausted", timestamp);
                RecordUnavailable();
                return LogOutputWriteStatus.Faulted;
            }

            long next = lastAcceptedSequence + 1;
            if (!channel.Writer.TryWrite(new AdmittedEvent(captured, next, preparedLine)))
            {
                if (queueFullCount != long.MaxValue)
                {
                    queueFullCount++;
                }

                return LogOutputWriteStatus.QueueFull;
            }

            lastAcceptedSequence = next;
            acceptedCount++;
            wake.TrySetResult();
            acceptedSequence = next;
            return LogOutputWriteStatus.Accepted;
        }
    }

    public void RecordFiltered()
    {
        lock (admissionGate)
        {
            if (filteredCount != long.MaxValue)
            {
                filteredCount++;
            }
        }
    }

    public void RecordUnavailable()
    {
        lock (admissionGate)
        {
            if (unavailableCount != long.MaxValue)
            {
                unavailableCount++;
            }
        }
    }

    // Primitive queue closure, not the public runtime's flush/deadline/shutdown protocol.
    internal void Complete()
    {
        lock (admissionGate)
        {
            stopRequested = true;
            admissionClosed = true;
            state = workerFinished ? LogOutputState.Stopped : LogOutputState.Stopping;
            channel.Writer.TryComplete();
            wake.TrySetResult();
            _ = CancelFileFlushScheduling();
        }
    }

    // Called under the shared runtime gate before the immutable deadline observation.
    internal void AbandonUnstarted(long? observedAt = null)
    {
        lock (admissionGate)
        {
            stopDeadlineExpired = true;
            while (channel.Reader.TryRead(out AdmittedEvent abandoned))
            {
                if (firstAbandonedSequence == 0)
                {
                    firstAbandonedSequence = abandoned.AcceptedSequence;
                }

                lastAbandonedSequence = abandoned.AcceptedSequence;
                abandonedCount++;
            }

            // A boundary covering abandoned tickets can never reach writtenCount.
            // Preserve already-started flush I/O until its actual completion.
            foreach (long target in flushRequests.Keys.ToArray())
            {
                OutputFlushRegistration registration = flushRequests[target];
                if (firstAbandonedSequence > 0 && target >= firstAbandonedSequence && !ReferenceEquals(registration, flushing))
                {
                    registration.Resolve(false);
                }
            }

            if (observedAt is { } timestamp)
            {
                ObserveRegistrations(timestamp);
            }

            NotifyDispositionChanged();
            wake.TrySetResult();
        }
    }

    internal void RequestIoCancellation()
    {
        if (ioCancellation is null)
        {
            return;
        }

        TaskCompletionSource completion;
        lock (admissionGate)
        {
            if (ioCancellationClosed || ioCancellationCompletion is not null)
            {
                return;
            }

            completion = NewWake();
            ioCancellationCompletion = completion.Task;
        }

        // Stream cancellation callbacks may block or throw. They run asynchronously,
        // outside admission synchronization, and cannot extend the stop deadline.
        _ = CancelAsync();
        async Task CancelAsync()
        {
            try
            {
                await ioCancellation.CancelAsync().ConfigureAwait(false);
                completion.TrySetResult();
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
            }
        }
    }

    private async Task CloseIoCancellationAsync()
    {
        Task? pending;
        lock (admissionGate)
        {
            ioCancellationClosed = true;
            pending = ioCancellationCompletion;
        }

        try
        {
            if (pending is not null)
            {
                await pending.ConfigureAwait(false);
            }
        }
        finally
        {
            ioCancellation?.Dispose();
        }
    }

    internal void FaultForRendering(long observedAt)
    {
        lock (admissionGate)
        {
            if (!faulted)
            {
                Fault("RenderTooLarge", observedAt);
            }
        }
    }

    // Caller holds the shared gate. Publish closure, safe metadata and all unstarted loss together.
    private void Fault(string code, long observedAt)
    {
        faulted = true;
        admissionClosed = true;
        _ = CancelFileFlushScheduling();
        state = stopRequested ? LogOutputState.Stopping : LogOutputState.Faulted;
        lastFaultCode = code;
        lastFaultAtUtc = DateTimeOffset.UtcNow;
        channel.Writer.TryComplete();
        while (channel.Reader.TryRead(out AdmittedEvent abandoned))
        {
            if (firstAbandonedSequence == 0)
            {
                firstAbandonedSequence = abandoned.AcceptedSequence;
            }

            lastAbandonedSequence = abandoned.AcceptedSequence;
            abandonedCount++;
        }

        NotifyDispositionChanged();
        foreach (OutputFlushRegistration registration in flushRequests.Values)
        {
            if (!ReferenceEquals(registration, flushing))
            {
                registration.Resolve(false, observedAt);
            }
        }

        // Retain controls only while covered in-flight work still needs a terminal timestamp.
        ObserveRegistrations(observedAt);
        wake.TrySetResult();
    }

    // Called only after queue closure or worker exit; cleanup does not reclassify delivered records or restart output work.
    internal void RecordCleanupFault(string code)
    {
        long observedAt = clock.GetTimestamp();
        lock (admissionGate)
        {
            faulted = true;
            admissionClosed = true;
            lastFaultCode = code;
            lastFaultAtUtc = DateTimeOffset.UtcNow;
            state = workerFinished && stopRequested ? LogOutputState.Stopped : stopRequested ? LogOutputState.Stopping : LogOutputState.Faulted;
            foreach (OutputFlushRegistration registration in flushRequests.Values)
            {
                if (!ReferenceEquals(registration, flushing))
                {
                    registration.Resolve(false, observedAt);
                }
            }

            // Worker observations retire controls when covered records and flushing settle.
            ObserveRegistrations(observedAt);
            wake.TrySetResult();
        }
    }

    private async Task ProcessAsync()
    {
        Exception? primaryFailure = null;
        try
        {
            while (true)
            {
                long observedAt = clock.GetTimestamp();
                bool expired;
                AdmittedEvent admitted = default;
                OutputFlushRegistration? request = null;
                Task? wait = null;
                lock (admissionGate)
                {
                    // Check the sampled budget and select work in the same gate section.
                    // Virtual clock calls remain outside the admission gate.
                    expired = stopBudget is { } budget && !budget.Contains(observedAt);
                    if (expired)
                    {
                        AbandonUnstarted(observedAt);
                    }

                    ObserveRegistrations(observedAt);
                    if (faulted)
                    {
                        workerExiting = true;
                        return;
                    }

                    if (stopDeadlineExpired)
                    {
                        // Expiry cancels active I/O; it never starts a later flush.
                        foreach (OutputFlushRegistration registration in flushRequests.Values)
                        {
                            registration.Resolve(false);
                        }

                        ObserveRegistrations(observedAt);
                    }

                    request = flushRequests.Values.FirstOrDefault(item => !item.WriterFlushCompletion.IsCompleted && item.AcceptedSequenceBoundary <= writtenCount);
                    if (request is not null)
                    {
                        flushing = request;
                    }
                    else if (channel.Reader.TryRead(out admitted))
                    {
                        inFlight = true;
                        inFlightSequence = admitted.AcceptedSequence;
                    }
                    else if (stopRequested)
                    {
                        workerExiting = true;
                        return;
                    }
                    else
                    {
                        // Reset only while no work is visible under the admission gate: no lost wakeup.
                        if (wake.Task.IsCompleted)
                        {
                            wake = NewWake();
                        }

                        wait = wake.Task;
                    }
                }

                if (expired)
                {
                    RequestIoCancellation();
                }

                if (wait is not null)
                {
                    await wait.ConfigureAwait(false);
                    continue;
                }

                if (request is not null)
                {
                    // Flush at this fixed boundary before starting a later record. All I/O is outside the gate.
                    await flush!(ioCancellation?.Token ?? CancellationToken.None).ConfigureAwait(false);
                    observedAt = clock.GetTimestamp();
                    lock (admissionGate)
                    {
                        flushing = null;
                        request.Resolve(true, observedAt);
                        ObserveRegistrations(observedAt);
                    }
                }
                else
                {
                    // Complete record confirmation only; a dequeued/partially emitted write is still pending.
                    await write(admitted, ioCancellation?.Token ?? CancellationToken.None).ConfigureAwait(false);
                    observedAt = clock.GetTimestamp();
                    lock (admissionGate)
                    {
                        writtenCount++;
                        inFlight = false;
                        ObserveRegistrations(observedAt);
                        NotifyDispositionChanged();
                    }
                }
            }
        }
        catch (Exception error)
        {
            primaryFailure = error;
            long observedAt = clock.GetTimestamp();
            lock (admissionGate)
            {
                if (inFlight)
                {
                    failedCount++;
                    failedSequence = inFlightSequence;
                    inFlight = false;
                }

                bool flushFailed = flushing is not null;
                flushing?.Resolve(false, observedAt);
                flushing = null;
                string code = error switch
                {
                    FileOutputWriteException fileError => fileError.FaultCode,
                    CoreLogInitializationException initializationError => initializationError.ErrorCode,
                    _ => "WriteFailed"
                };
                Fault(flushFailed ? "FlushFailed" : code, observedAt);
                ObserveRegistrations(observedAt);
            }

            throw;
        }
        finally
        {
            // No pending writer call remains. Owned cleanup precedes terminal state/Completion,
            // outside the gate, and never receives a borrowed console stream.
            lock (admissionGate)
            {
                workerExiting = true;
            }

            List<Exception>? cleanupFailures = null;
            try
            {
                await CloseIoCancellationAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                RecordCleanupFault("CancellationCallbacksFailed");
                (cleanupFailures ??= []).Add(error);
            }

            try
            {
                await CancelFileFlushScheduling().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                RecordCleanupFault("FileFlushTimerCleanupFailed");
                (cleanupFailures ??= []).Add(error);
            }

            try
            {
                ownedResource?.Dispose();
                if (cleanup is not null) { await cleanup().ConfigureAwait(false); }
            }
            catch (Exception error)
            {
                RecordCleanupFault("OutputCleanupFailed");
                (cleanupFailures ??= []).Add(error);
            }

            lock (admissionGate)
            {
                admissionClosed = true;
                channel.Writer.TryComplete();
                workerExiting = true;
                workerFinished = true;
                state = stopRequested || !faulted ? LogOutputState.Stopped : LogOutputState.Faulted;
            }

            if (cleanupFailures is not null)
            {
                // Preserve the exact original I/O failure as well as every failed cleanup step.
                if (primaryFailure is not null)
                {
                    cleanupFailures.Insert(0, primaryFailure);
                }

                if (cleanupFailures.Count == 1)
                {
                    ExceptionDispatchInfo.Capture(cleanupFailures[0]).Throw();
                }

                throw new AggregateException("Output cleanup failed.", cleanupFailures);
            }
        }
    }
}

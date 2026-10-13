using System.Threading.Channels;
using CIS.Logging.Events;

namespace CIS.Logging.Runtime;

// One queue and one awaited writer operation. Only trusted internal composition supplies the writer.
internal sealed class OutputEventQueue : ILogOutputAdmission
{
    private readonly object admissionGate;
    private readonly Channel<AdmittedEvent> channel;
    private readonly Func<AdmittedEvent, ValueTask> write;
    private readonly IDisposable? ownedResource;
    private readonly Func<ValueTask>? flush;
    private readonly SortedDictionary<long, OutputFlushRegistration> flushRequests = [];
    private TaskCompletionSource wake = NewWake();
    private OutputFlushRegistration? flushing;
    private bool workerExiting;
    private long lastAcceptedSequence;
    private long acceptedCount;
    private long queueFullCount;
    private long writtenCount;
    private long failedCount;
    private long abandonedCount;
    private bool inFlight;
    private long filteredCount;
    private long unavailableCount;
    private bool admissionClosed;
    private bool faulted;
    private bool stopRequested;
    private bool workerFinished;
    private LogOutputState state;
    private string? lastFaultCode;
    private DateTimeOffset? lastFaultAtUtc;
    internal OutputEventQueue(int capacity, Func<AdmittedEvent, ValueTask> write, object? sharedAdmissionGate = null, Func<Func<Task>, Task>? launchWorker = null, IDisposable? ownedResource = null, Func<ValueTask>? flush = null)
    {
        ArgumentNullException.ThrowIfNull(write);
        if (capacity is < LoggingOptions.MinimumQueueCapacity or > LoggingOptions.MaximumQueueCapacity) { throw new ArgumentOutOfRangeException(nameof(capacity)); }
        admissionGate = sharedAdmissionGate ?? new object();
        this.write = write;
        this.ownedResource = ownedResource;
        this.flush = flush;
        channel = Channel.CreateBounded<AdmittedEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        // Captured events already contain their context. A worker must not retain its creator's flow.
        // Trusted composition launchers schedule this actual worker or throw before acquiring a task.
        // Production always uses Task.Run; the internal seam permits deterministic acquisition failure.
        Task Launch() => (launchWorker is null ? Task.Run(ProcessAsync) : launchWorker(ProcessAsync))
            ?? throw new InvalidOperationException("Worker launch must return its scheduled task.");
        if (ExecutionContext.IsFlowSuppressed()) { Completion = Launch(); }
        else { using (ExecutionContext.SuppressFlow()) { Completion = Launch(); } }
    }
    private static TaskCompletionSource NewWake() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task Completion { get; }
    internal int RegisteredFlushCount { get { lock (admissionGate) { return flushRequests.Count; } } }
    internal OutputFlushBoundary RegisterFlushBoundary()
    {
        lock (admissionGate)
        {
            long boundary = lastAcceptedSequence;
            if (!flushRequests.TryGetValue(boundary, out OutputFlushRegistration? registration))
            {
                registration = new OutputFlushRegistration(boundary);
                if (faulted || workerExiting || workerFinished || flush is null) { registration.Resolve(false); }
                else
                {
                    // Coordination is separate from bounded event slots and cannot overflow with them.
                    flushRequests.Add(boundary, registration);
                    wake.TrySetResult();
                }
            }
            return new OutputFlushBoundary(boundary, filteredCount, queueFullCount, unavailableCount, registration);
        }
    }
    internal int BufferedCount { get { lock (admissionGate) { return channel.Reader.Count; } } }
    internal long LastAcceptedSequence { get { lock (admissionGate) { return lastAcceptedSequence; } } }
    internal long FilteredCount { get { lock (admissionGate) { return filteredCount; } } }
    internal long UnavailableCount { get { lock (admissionGate) { return unavailableCount; } } }
    internal OutputAccounting CaptureAccounting()
    {
        lock (admissionGate)
        {
            return new OutputAccounting(filteredCount, acceptedCount, queueFullCount, unavailableCount,
                writtenCount, failedCount, abandonedCount, channel.Reader.Count + (inFlight ? 1 : 0), inFlight ? 1 : 0, lastAcceptedSequence, state, lastFaultCode, lastFaultAtUtc);
        }
    }
    public bool IsFaulted { get { lock (admissionGate) { return faulted; } } }
    internal bool TryEnqueue(EncodedEvent captured) => TryAdmit(captured, out _) == LogOutputWriteStatus.Accepted;
    public LogOutputWriteStatus TryAdmit(EncodedEvent captured, out long? acceptedSequence) => TryAdmit(captured, null, out acceptedSequence);
    internal LogOutputWriteStatus TryAdmit(EncodedEvent captured, ReadOnlyMemory<byte>? preparedLine, out long? acceptedSequence)
    {
        ArgumentNullException.ThrowIfNull(captured);
        lock (admissionGate)
        {
            acceptedSequence = null;
            if (admissionClosed)
            {
                if (faulted) { RecordUnavailable(); return LogOutputWriteStatus.Faulted; }
                return LogOutputWriteStatus.Stopped;
            }
            if (lastAcceptedSequence == long.MaxValue || acceptedCount == long.MaxValue)
            {
                // No wrap, negative/duplicate ticket or invented acceptance. Exhaustion is terminal.
                Fault("AdmissionExhausted"); RecordUnavailable();
                return LogOutputWriteStatus.Faulted;
            }
            long next = lastAcceptedSequence + 1;
            if (!channel.Writer.TryWrite(new AdmittedEvent(captured, next, preparedLine)))
            {
                if (queueFullCount != long.MaxValue) { queueFullCount++; }
                return LogOutputWriteStatus.QueueFull;
            }
            lastAcceptedSequence = next;
            acceptedCount++;
            wake.TrySetResult();
            acceptedSequence = next;
            return LogOutputWriteStatus.Accepted;
        }
    }
    public void RecordFiltered() { lock (admissionGate) { if (filteredCount != long.MaxValue) { filteredCount++; } } }
    public void RecordUnavailable() { lock (admissionGate) { if (unavailableCount != long.MaxValue) { unavailableCount++; } } }
    // Primitive queue closure, not the public runtime's flush/deadline/shutdown protocol.
    internal void Complete()
    {
        lock (admissionGate)
        {
            stopRequested = true; admissionClosed = true;
            state = workerFinished ? LogOutputState.Stopped : LogOutputState.Stopping;
            channel.Writer.TryComplete();
            wake.TrySetResult();
        }
    }
    // Caller holds the shared gate. Publish closure, safe metadata and all unstarted loss together.
    private void Fault(string code)
    {
        faulted = true; admissionClosed = true;
        state = stopRequested ? LogOutputState.Stopping : LogOutputState.Faulted;
        lastFaultCode = code; lastFaultAtUtc = DateTimeOffset.UtcNow;
        channel.Writer.TryComplete();
        while (channel.Reader.TryRead(out _)) { abandonedCount++; }
        foreach (OutputFlushRegistration registration in flushRequests.Values)
        {
            if (!ReferenceEquals(registration, flushing)) { registration.Resolve(false); }
        }
        OutputFlushRegistration? active = flushing;
        flushRequests.Clear();
        // An already-started flush remains unresolved until its actual I/O settles, even if admission faults.
        if (active is not null) { flushRequests.Add(active.AcceptedSequenceBoundary, active); }
        wake.TrySetResult();
    }
    private async Task ProcessAsync()
    {
        try
        {
            while (true)
            {
                AdmittedEvent admitted = default;
                OutputFlushRegistration? request = null;
                Task? wait = null;
                lock (admissionGate)
                {
                    if (faulted) { workerExiting = true; return; }
                    if (flushRequests.Count > 0 && flushRequests.First().Key <= writtenCount)
                    {
                        request = flushRequests.First().Value;
                        flushing = request;
                    }
                    else if (channel.Reader.TryRead(out admitted)) { inFlight = true; }
                    else if (stopRequested) { workerExiting = true; return; }
                    else
                    {
                        // Reset only while no work is visible under the admission gate: no lost wakeup.
                        if (wake.Task.IsCompleted) { wake = NewWake(); }
                        wait = wake.Task;
                    }
                }
                if (wait is not null) { await wait.ConfigureAwait(false); continue; }
                if (request is not null)
                {
                    // Flush at this fixed boundary before starting a later record. All I/O is outside the gate.
                    await flush!().ConfigureAwait(false);
                    lock (admissionGate)
                    {
                        flushRequests.Remove(request.AcceptedSequenceBoundary);
                        flushing = null;
                        request.Resolve(true);
                    }
                }
                else
                {
                    // Complete record confirmation only; a dequeued/partially emitted write is still pending.
                    await write(admitted).ConfigureAwait(false);
                    lock (admissionGate) { writtenCount++; inFlight = false; }
                }
            }
        }
        catch (Exception error)
        {
            lock (admissionGate)
            {
                if (inFlight) { failedCount++; inFlight = false; }
                bool flushFailed = flushing is not null;
                flushing?.Resolve(false);
                flushing = null;
                Fault(flushFailed ? "FlushFailed" : error is FileOutputWriteException fileError ? fileError.FaultCode : "WriteFailed");
            }
            throw;
        }
        finally
        {
            // No pending writer call remains. Owned cleanup precedes terminal state/Completion,
            // outside the gate, and never receives a borrowed console stream.
            try { ownedResource?.Dispose(); }
            finally
            {
                lock (admissionGate)
                {
                    admissionClosed = true; channel.Writer.TryComplete();
                    workerExiting = true;
                    workerFinished = true;
                    state = stopRequested || !faulted ? LogOutputState.Stopped : LogOutputState.Faulted;
                }
            }
        }
    }
}

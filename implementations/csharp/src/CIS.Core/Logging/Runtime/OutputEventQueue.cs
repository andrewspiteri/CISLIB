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
    internal OutputEventQueue(int capacity, Func<AdmittedEvent, ValueTask> write, object? sharedAdmissionGate = null, Func<Func<Task>, Task>? launchWorker = null, IDisposable? ownedResource = null)
    {
        ArgumentNullException.ThrowIfNull(write);
        if (capacity is < LoggingOptions.MinimumQueueCapacity or > LoggingOptions.MaximumQueueCapacity) { throw new ArgumentOutOfRangeException(nameof(capacity)); }
        admissionGate = sharedAdmissionGate ?? new object();
        this.write = write;
        this.ownedResource = ownedResource;
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
    internal Task Completion { get; }
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
    }
    private async Task ProcessAsync()
    {
        try
        {
            while (await channel.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (true)
                {
                    AdmittedEvent admitted;
                    lock (admissionGate)
                    {
                        if (faulted) { return; }
                        if (!channel.Reader.TryRead(out admitted)) { break; }
                        inFlight = true;
                    }
                    // Taking work under the gate observes its published ticket. I/O is outside it.
                    // The trusted callback confirms the complete record only by successful completion.
                    // Unresolved/partially emitted bytes remain pending; a terminal exception is failed.
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
                Fault(error is FileOutputWriteException fileError ? fileError.FaultCode : "WriteFailed");
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
                    workerFinished = true;
                    state = stopRequested || !faulted ? LogOutputState.Stopped : LogOutputState.Faulted;
                }
            }
        }
    }
}

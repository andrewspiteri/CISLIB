using System.Threading.Channels;
using CIS.Logging.Events;

namespace CIS.Logging.Runtime;

// One queue and one awaited writer operation. Only trusted internal composition supplies the writer.
internal sealed class OutputEventQueue : ILogOutputAdmission
{
    private readonly object admissionGate;
    private readonly Channel<AdmittedEvent> channel;
    private readonly Func<AdmittedEvent, ValueTask> write;
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
    internal OutputEventQueue(int capacity, Func<AdmittedEvent, ValueTask> write, object? sharedAdmissionGate = null)
    {
        ArgumentNullException.ThrowIfNull(write);
        if (capacity is < LoggingOptions.MinimumQueueCapacity or > LoggingOptions.MaximumQueueCapacity) { throw new ArgumentOutOfRangeException(nameof(capacity)); }
        admissionGate = sharedAdmissionGate ?? new object();
        this.write = write;
        channel = Channel.CreateBounded<AdmittedEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        // Captured events already contain their context. A worker must not retain its creator's flow.
        if (ExecutionContext.IsFlowSuppressed()) { Completion = Task.Run(ProcessAsync); }
        else { using (ExecutionContext.SuppressFlow()) { Completion = Task.Run(ProcessAsync); } }
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
                writtenCount, failedCount, abandonedCount, channel.Reader.Count + (inFlight ? 1 : 0), inFlight ? 1 : 0, lastAcceptedSequence);
        }
    }
    public bool IsFaulted { get { lock (admissionGate) { return faulted; } } }
    internal bool TryEnqueue(EncodedEvent captured) => TryAdmit(captured, out _) == LogOutputWriteStatus.Accepted;
    public LogOutputWriteStatus TryAdmit(EncodedEvent captured, out long? acceptedSequence)
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
                faulted = true; admissionClosed = true; channel.Writer.TryComplete(); RecordUnavailable();
                return LogOutputWriteStatus.Faulted;
            }
            long next = lastAcceptedSequence + 1;
            if (!channel.Writer.TryWrite(new AdmittedEvent(captured, next)))
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
    internal void Complete() { lock (admissionGate) { admissionClosed = true; channel.Writer.TryComplete(); } }
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
        catch
        {
            lock (admissionGate)
            {
                if (inFlight) { failedCount++; inFlight = false; }
                faulted = true; admissionClosed = true; channel.Writer.TryComplete();
            }
            throw;
        }
        finally
        {
            lock (admissionGate)
            {
                Complete();
                // Not-started accepted work is abandoned, never promoted to confirmed writes.
                while (channel.Reader.TryRead(out _)) { abandonedCount++; }
            }
        }
    }
}

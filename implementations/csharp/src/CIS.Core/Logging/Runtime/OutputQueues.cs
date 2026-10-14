namespace CIS.Logging.Runtime;

// Creates exactly one independent worker for each enabled destination and no disabled worker.
internal sealed class OutputQueues
{
    private readonly object admissionGate;
    internal OutputQueues(int capacity, bool consoleEnabled, bool fileEnabled,
        Func<AdmittedEvent, ValueTask>? consoleWrite, Func<AdmittedEvent, ValueTask>? fileWrite, object? sharedAdmissionGate = null, Func<Func<Task>, Task>? launchWorker = null, IDisposable? fileResource = null, Func<ValueTask>? consoleFlush = null, Func<ValueTask>? fileFlush = null,
        Func<AdmittedEvent, CancellationToken, ValueTask>? consoleCancellableWrite = null, Func<AdmittedEvent, CancellationToken, ValueTask>? fileCancellableWrite = null,
        Func<CancellationToken, ValueTask>? consoleCancellableFlush = null, Func<CancellationToken, ValueTask>? fileCancellableFlush = null)
    {
        admissionGate = sharedAdmissionGate ?? new object();
        if (capacity is < LoggingOptions.MinimumQueueCapacity or > LoggingOptions.MaximumQueueCapacity) { throw new ArgumentOutOfRangeException(nameof(capacity)); }
        if (consoleEnabled != (consoleWrite is not null)) { throw new ArgumentException("Writer presence must match enabled console output.", nameof(consoleWrite)); }
        if (fileEnabled != (fileWrite is not null)) { throw new ArgumentException("Writer presence must match enabled file output.", nameof(fileWrite)); }
        if (!consoleEnabled && consoleFlush is not null) { throw new ArgumentException("Disabled console output cannot have a flush callback.", nameof(consoleFlush)); }
        if (!fileEnabled && fileFlush is not null) { throw new ArgumentException("Disabled file output cannot have a flush callback.", nameof(fileFlush)); }
        try
        {
            if (consoleWrite is not null) { Console = Create("console", consoleWrite, null, consoleFlush, consoleCancellableWrite, consoleCancellableFlush); }
            if (fileWrite is not null) { File = Create("file", fileWrite, fileResource, fileFlush, fileCancellableWrite, fileCancellableFlush); }
            Completion = Task.WhenAll(Console?.Completion ?? Task.CompletedTask, File?.Completion ?? Task.CompletedTask);
        }
        catch
        {
            // Unpublished queues are empty. Close/join only this attempt's idle workers, outside the gate.
            Complete();
            try { Task.WhenAll(Console?.Completion ?? Task.CompletedTask, File?.Completion ?? Task.CompletedTask).GetAwaiter().GetResult(); } catch { }
            throw;
        }
        OutputEventQueue Create(string name, Func<AdmittedEvent, ValueTask> write, IDisposable? ownedResource, Func<ValueTask>? flush, Func<AdmittedEvent, CancellationToken, ValueTask>? cancellableWrite, Func<CancellationToken, ValueTask>? cancellableFlush)
        {
            try { return new OutputEventQueue(capacity, write, admissionGate, launchWorker, ownedResource, flush, cancellableWrite, cancellableFlush); }
            catch { throw new CoreLogInitializationException(name, "WorkerInitializationFailed"); }
        }
    }
    internal OutputEventQueue? Console { get; }
    internal OutputEventQueue? File { get; }
    internal Task Completion { get; }
    internal void Complete() { lock (admissionGate) { Console?.Complete(); File?.Complete(); } }
    internal void AbandonUnstarted() { lock (admissionGate) { Console?.AbandonUnstarted(); File?.AbandonUnstarted(); } }
    internal void RequestIoCancellation() { Console?.RequestIoCancellation(); File?.RequestIoCancellation(); }
    internal void RecordCleanupFault(string code) { Console?.RecordCleanupFault(code); File?.RecordCleanupFault(code); }
}

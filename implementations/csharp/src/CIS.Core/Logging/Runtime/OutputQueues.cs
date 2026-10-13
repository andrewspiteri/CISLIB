namespace CIS.Logging.Runtime;

// Creates exactly one independent worker for each enabled destination and no disabled worker.
internal sealed class OutputQueues
{
    private readonly object admissionGate;
    internal OutputQueues(int capacity, bool consoleEnabled, bool fileEnabled,
        Func<AdmittedEvent, ValueTask>? consoleWrite, Func<AdmittedEvent, ValueTask>? fileWrite, object? sharedAdmissionGate = null, Func<Func<Task>, Task>? launchWorker = null)
    {
        admissionGate = sharedAdmissionGate ?? new object();
        if (capacity is < LoggingOptions.MinimumQueueCapacity or > LoggingOptions.MaximumQueueCapacity) { throw new ArgumentOutOfRangeException(nameof(capacity)); }
        if (consoleEnabled != (consoleWrite is not null)) { throw new ArgumentException("Writer presence must match enabled console output.", nameof(consoleWrite)); }
        if (fileEnabled != (fileWrite is not null)) { throw new ArgumentException("Writer presence must match enabled file output.", nameof(fileWrite)); }
        try
        {
            if (consoleWrite is not null) { Console = Create("console", consoleWrite); }
            if (fileWrite is not null) { File = Create("file", fileWrite); }
            Completion = Task.WhenAll(Console?.Completion ?? Task.CompletedTask, File?.Completion ?? Task.CompletedTask);
        }
        catch
        {
            // Unpublished queues are empty. Close/join only this attempt's idle workers, outside the gate.
            Complete(); Console?.Completion.GetAwaiter().GetResult(); File?.Completion.GetAwaiter().GetResult(); throw;
        }
        OutputEventQueue Create(string name, Func<AdmittedEvent, ValueTask> write)
        {
            try { return new OutputEventQueue(capacity, write, admissionGate, launchWorker); }
            catch { throw new CoreLogInitializationException(name, "WorkerInitializationFailed"); }
        }
    }
    internal OutputEventQueue? Console { get; }
    internal OutputEventQueue? File { get; }
    internal Task Completion { get; }
    internal void Complete() { lock (admissionGate) { Console?.Complete(); File?.Complete(); } }
}

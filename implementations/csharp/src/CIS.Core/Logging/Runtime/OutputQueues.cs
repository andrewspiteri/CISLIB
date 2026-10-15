namespace CIS.Logging.Runtime;
// Creates exactly one independent worker for each enabled destination and no disabled worker.
internal sealed class OutputQueues
{
    private readonly object admissionGate;
    private readonly List<(string Name, OutputEventQueue Queue)> entries = [];
    private Task? completion;
    internal IReadOnlyList<(string Name, OutputEventQueue Queue)> Entries => entries;
    internal OutputEventQueue Get(string name) => entries.Single(entry => entry.Name == name).Queue;
    internal OutputQueues(int capacity, IReadOnlyList<OutputWriterBinding> writers, object sharedAdmissionGate,
        Func<Func<Task>, Task>? launchWorker = null, TimeProvider? timeProvider = null)
    {
        admissionGate = sharedAdmissionGate;
        if (capacity is < LoggingOptions.MinimumQueueCapacity or > LoggingOptions.MaximumQueueCapacity)
        { throw new ArgumentOutOfRangeException(nameof(capacity)); }
        try
        {
            foreach (var writer in writers)
            {
                OutputEventQueue queue;
                try
                {
                    queue = new OutputEventQueue(capacity, item => writer.Write(item, CancellationToken.None), admissionGate,
                        launchWorker, cancellableWrite: writer.Write, cancellableFlush: writer.Flush,
                        timeProvider: timeProvider, cleanup: writer.Cleanup);
                }
                catch { throw new CoreLogInitializationException(writer.Name, "WorkerInitializationFailed"); }
                Add(writer.Name, queue);
            }
        }
        catch
        {
            Complete();
            try { Completion.GetAwaiter().GetResult(); } catch { }
            throw;
        }
    }

    // Legacy paired qualification seam. Production supplies the common writer collection above.
    internal OutputQueues(int capacity, bool consoleEnabled, bool fileEnabled, Func<AdmittedEvent, ValueTask>? consoleWrite, Func<AdmittedEvent, ValueTask>? fileWrite, object? sharedAdmissionGate = null, Func<Func<Task>, Task>? launchWorker = null, IDisposable? fileResource = null, Func<ValueTask>? consoleFlush = null, Func<ValueTask>? fileFlush = null, Func<AdmittedEvent, CancellationToken, ValueTask>? consoleCancellableWrite = null, Func<AdmittedEvent, CancellationToken, ValueTask>? fileCancellableWrite = null, Func<CancellationToken, ValueTask>? consoleCancellableFlush = null, Func<CancellationToken, ValueTask>? fileCancellableFlush = null, TimeProvider? timeProvider = null)
    {
        admissionGate = sharedAdmissionGate ?? new object();
        if (capacity is < LoggingOptions.MinimumQueueCapacity or > LoggingOptions.MaximumQueueCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        if (consoleEnabled != (consoleWrite is not null))
        {
            throw new ArgumentException("Writer presence must match enabled console output.", nameof(consoleWrite));
        }

        if (fileEnabled != (fileWrite is not null))
        {
            throw new ArgumentException("Writer presence must match enabled file output.", nameof(fileWrite));
        }

        if (!consoleEnabled && consoleFlush is not null)
        {
            throw new ArgumentException("Disabled console output cannot have a flush callback.", nameof(consoleFlush));
        }

        if (!fileEnabled && fileFlush is not null)
        {
            throw new ArgumentException("Disabled file output cannot have a flush callback.", nameof(fileFlush));
        }

        try
        {
            if (consoleWrite is not null)
            {
                entries.Add(("console", Create("console", consoleWrite, null, consoleFlush, consoleCancellableWrite, consoleCancellableFlush)));
            }

            if (fileWrite is not null)
            {
                entries.Add(("file", Create("file", fileWrite, fileResource, fileFlush, fileCancellableWrite, fileCancellableFlush)));
            }

        }
        catch
        {
            // Unpublished queues are empty. Close/join only this attempt's idle workers, outside the gate.
            Complete();
            try
            {
                Task.WhenAll(entries.Select(entry => entry.Queue.Completion)).GetAwaiter().GetResult();
            }
            catch
            {
            }

            throw;
        }

        OutputEventQueue Create(string name, Func<AdmittedEvent, ValueTask> write, IDisposable? ownedResource, Func<ValueTask>? flush, Func<AdmittedEvent, CancellationToken, ValueTask>? cancellableWrite, Func<CancellationToken, ValueTask>? cancellableFlush)
        {
            try
            {
                return new OutputEventQueue(capacity, write, admissionGate, launchWorker, ownedResource, flush, cancellableWrite, cancellableFlush, timeProvider);
            }
            catch
            {
                throw new CoreLogInitializationException(name, "WorkerInitializationFailed");
            }
        }
    }

    internal OutputEventQueue? Console => entries.FirstOrDefault(entry => entry.Name == "console").Queue;
    internal OutputEventQueue? File => entries.FirstOrDefault(entry => entry.Name == "file").Queue;
    internal Task Completion
    {
        get { lock (admissionGate) { return completion ??= Task.WhenAll(entries.Select(entry => entry.Queue.Completion)); } }
    }

    // Startup only, before this collection is published to callers.
    internal void Add(string name, OutputEventQueue queue)
    {
        if (entries.Any(entry => entry.Name == name)) { throw new ArgumentException("Duplicate output name.", nameof(name)); }
        if (completion is not null) { throw new InvalidOperationException("Writer startup is already sealed."); }
        entries.Add((name, queue));
    }

    internal void SetStopBudget(ElapsedBudget budget)
    {
        lock (admissionGate)
        {
            foreach (var entry in entries) { entry.Queue.SetStopBudget(budget); }
        }
    }

    internal void Complete()
    {
        lock (admissionGate)
        {
            foreach (var entry in entries) { entry.Queue.Complete(); }
        }
    }

    internal void AbandonUnstarted(long observedAt)
    {
        lock (admissionGate)
        {
            foreach (var entry in entries) { entry.Queue.AbandonUnstarted(observedAt); }
        }
    }

    internal void RequestIoCancellation()
    {
        foreach (var entry in entries) { entry.Queue.RequestIoCancellation(); }
    }

    internal void RecordCleanupFault(string code)
    {
        foreach (var entry in entries) { entry.Queue.RecordCleanupFault(code); }
    }
}

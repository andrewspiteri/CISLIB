namespace CIS.Logging.Runtime;
// Immutable initial facts. The shared registration resolves writer flushing, not a public drain result.
internal readonly record struct RuntimeFlushBoundary(DateTimeOffset BoundaryCapturedAtUtc, long InvalidEventCount, long TruncatedEventCount, OutputFlushBoundary? Console, OutputFlushBoundary? File, IReadOnlyList<NamedOutputFlushBoundary>? Additional = null)
{
    internal IEnumerable<NamedOutputFlushBoundary> Entries
    {
        get
        {
            if (Console is { } console) { yield return new("console", console); }
            if (File is { } file) { yield return new("file", file); }
            if (Additional is not null) { foreach (var entry in Additional) { yield return entry; } }
        }
    }
    // The caller supplies its lifetime token (cancellation or deadline). Never retain a per-caller WhenAll.
    internal async Task<bool> WaitForWriterFlushAsync(CancellationToken waitCancellation)
    {
        waitCancellation.ThrowIfCancellationRequested();
        bool succeeded = true;
        foreach (var entry in Entries)
        { succeeded &= await entry.Boundary.Registration.WaitForWriterFlushAsync(waitCancellation).ConfigureAwait(false); }
        return succeeded;
    }
}

internal readonly record struct NamedOutputFlushBoundary(string Name, OutputFlushBoundary Boundary);
internal readonly record struct OutputFlushBoundary(long AcceptedSequenceBoundary, long Filtered, long QueueFull, long Unavailable, OutputFlushRegistration Registration);
internal sealed class OutputFlushRegistration(long boundary)
{
    private readonly TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal long AcceptedSequenceBoundary { get; } = boundary;
    internal Task<bool> WriterFlushCompletion => completion.Task;

    internal Task<bool> WaitForWriterFlushAsync(CancellationToken waitCancellation)
    {
        // Already-cancelled tokens throw synchronously here; the runtime async wrapper returns a cancelled task.
        waitCancellation.ThrowIfCancellationRequested();
        return completion.Task.WaitAsync(waitCancellation);
    }

    private long? writerSettledAt;
    private long? recordsSettledAt;
    // Accessed under the output admission gate, never by caller continuations.
    internal long? SettledAt => writerSettledAt is { } writer && recordsSettledAt is { } records ? Math.Max(writer, records) : null;

    internal void Resolve(bool succeeded, long? observedAt = null)
    {
        if (completion.TrySetResult(succeeded))
        {
            writerSettledAt = observedAt;
        }
    }

    internal void Observe(long timestamp, bool recordsSettled)
    {
        if (completion.Task.IsCompleted)
        {
            writerSettledAt ??= timestamp;
        }

        if (recordsSettled)
        {
            recordsSettledAt ??= timestamp;
        }
    }
}

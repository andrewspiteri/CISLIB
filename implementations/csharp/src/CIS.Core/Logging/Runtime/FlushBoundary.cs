namespace CIS.Logging.Runtime;

// Immutable initial facts. The shared registration resolves writer flushing, not a public drain result.
internal readonly record struct RuntimeFlushBoundary(DateTimeOffset BoundaryCapturedAtUtc,
    long InvalidEventCount, long TruncatedEventCount, OutputFlushBoundary? Console, OutputFlushBoundary? File)
{
    // The caller supplies its lifetime token (cancellation or deadline). Never retain a per-caller WhenAll.
    internal async Task<bool> WaitForWriterFlushAsync(CancellationToken waitCancellation)
    {
        waitCancellation.ThrowIfCancellationRequested();
        bool consoleSucceeded = true, fileSucceeded = true;
        if (Console is { } console)
        { consoleSucceeded = await console.Registration.WaitForWriterFlushAsync(waitCancellation).ConfigureAwait(false); }
        if (File is { } file)
        { fileSucceeded = await file.Registration.WaitForWriterFlushAsync(waitCancellation).ConfigureAwait(false); }
        return consoleSucceeded && fileSucceeded;
    }
}

internal readonly record struct OutputFlushBoundary(long AcceptedSequenceBoundary, long Filtered,
    long QueueFull, long Unavailable, OutputFlushRegistration Registration);

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
    internal void Resolve(bool succeeded) => completion.TrySetResult(succeeded);
}

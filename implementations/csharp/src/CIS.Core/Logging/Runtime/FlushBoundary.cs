namespace CIS.Logging.Runtime;

// Immutable initial facts. The shared registration resolves writer flushing, not a public drain result.
internal readonly record struct RuntimeFlushBoundary(DateTimeOffset BoundaryCapturedAtUtc,
    long InvalidEventCount, long TruncatedEventCount, OutputFlushBoundary? Console, OutputFlushBoundary? File);

internal readonly record struct OutputFlushBoundary(long AcceptedSequenceBoundary, long Filtered,
    long QueueFull, long Unavailable, OutputFlushRegistration Registration);

internal sealed class OutputFlushRegistration(long boundary)
{
    private readonly TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal long AcceptedSequenceBoundary { get; } = boundary;
    internal Task<bool> WriterFlushCompletion => completion.Task;
    internal void Resolve(bool succeeded) => completion.TrySetResult(succeeded);
}

using System.Collections.Concurrent;
using CIS.RestClient;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.RestClient;

internal static class ResilienceTestSupport
{
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    internal static RestRetryExecutionContext Context(TimeProvider clock, CancellationToken token = default,
        TimeSpan? timeout = null, RestReplaySafety replay = RestReplaySafety.Default, Func<bool>? stopping = null)
        => new(clock, clock.GetTimestamp(), timeout ?? TimeSpan.FromSeconds(30), token, "GET", replay, stopping ?? (() => false));
    internal static RestAttemptOutcome Http(string? retryAfter = null, string? method = "GET", bool replayable = true)
        => new(503, RestFailureKind.Http, "http.503", retryAfter, BodyProcessingStarted: true, BodyReplayable: replayable, Method: method);
    internal static RestAttemptOutcome Success() => new(200, RestFailureKind.None, Method: "GET");
    internal static async Task SettleAsync(Task? task)
    {
        if (task is null) { return; }
        try { await task.WaitAsync(Deadline); }
        catch (OperationCanceledException) when (task.IsCanceled) { }
    }
}

// Reuses the existing manual clock. The private observer records actual native timer inputs, not a replacement wait algorithm.
internal sealed class ResilienceClock : TimeProvider, IDisposable
{
    private readonly ManualTimeProvider manual = new(new DateTimeOffset(2026, 11, 17, 22, 0, 0, TimeSpan.Zero));
    private readonly ConcurrentQueue<Wait> waits = new();
    private readonly SemaphoreSlim ready = new(0);
    private int created;
    public override long TimestampFrequency => manual.TimestampFrequency;
    public override long GetTimestamp() => manual.GetTimestamp();
    public override DateTimeOffset GetUtcNow() => manual.GetUtcNow();
    internal int Created => Volatile.Read(ref created);
    internal void Advance(TimeSpan elapsed) => manual.Advance(elapsed);
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ITimer timer = manual.CreateTimer(callback, state, dueTime, period);
        var wait = new Wait(dueTime);
        Interlocked.Increment(ref created); waits.Enqueue(wait); ready.Release();
        return new ObservedTimer(timer, wait);
    }
    internal async Task<Wait> NextAsync()
    {
        Assert.IsTrue(await ready.WaitAsync(ResilienceTestSupport.Deadline), "The actual native retry wait must be scheduled.");
        Assert.IsTrue(waits.TryDequeue(out Wait? wait));
        return wait!;
    }
    public void Dispose() => ready.Dispose();
    internal sealed class Wait(TimeSpan delay)
    {
        internal TimeSpan Delay { get; } = delay;
        internal TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class ObservedTimer(ITimer inner, Wait wait) : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => inner.Change(dueTime, period);
        public void Dispose() { inner.Dispose(); wait.Disposed.TrySetResult(); }
        public async ValueTask DisposeAsync() { await inner.DisposeAsync(); wait.Disposed.TrySetResult(); }
    }
}

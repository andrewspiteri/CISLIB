using System.Collections.Concurrent;
using CIS.Heartbeat;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Heartbeat;

internal sealed class HeartbeatTestScope : IAsyncDisposable
{
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private readonly ConcurrentBag<Action> releases = [];

    internal HeartbeatTestScope(int capacity = 1)
    {
        Clock = new HeartbeatTestClock();
        Executor = new HeartbeatControlledExecutor();
        Runtime = new HeartbeatRuntime(new HeartbeatSettings(capacity), Clock, Executor);
        Owner = Runtime.CreateOwner();
    }

    internal HeartbeatRuntime Runtime { get; }
    internal HeartbeatOwner Owner { get; }
    internal HeartbeatTestClock Clock { get; }
    internal HeartbeatControlledExecutor Executor { get; }

    internal TaskCompletionSource Hold()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        releases.Add(() => release.TrySetResult());
        return release;
    }

    internal TaskCompletionSource<bool> HoldResult()
    {
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        releases.Add(() => release.TrySetResult(true));
        return release;
    }

    internal HeartbeatRecordingObserver Register(HeartbeatProbe subscriber,
        HeartbeatRegistrationOptions? options = null,
        Func<CancellationToken, Task<bool>>? condition = null,
        Func<ValueTask>? cleanup = null,
        HeartbeatRecordingObserver? observer = null,
        HeartbeatOwner? owner = null,
        CancellationToken executionCancellation = default)
    {
        observer ??= new HeartbeatRecordingObserver();
        Runtime.Register(owner ?? Owner, subscriber, options ?? new(), condition, _ => observer, cleanup, executionCancellation);
        return observer;
    }

    internal void Activate(int posted = 1)
    {
        Runtime.Activate();
        WaitUntil(() => Executor.PendingCount == posted, "Expected wrappers were not posted.");
    }

    internal void WaitSettled(HeartbeatRecordingObserver observer, int executions = 1)
        => WaitUntil(() => observer.Count(HeartbeatEvent.Settled) == executions, "Actual execution settlement was not observed.");

    internal static void WaitUntil(Func<bool> condition, string message)
        => Assert.IsTrue(SpinWait.SpinUntil(condition, Deadline), message);

    public async ValueTask DisposeAsync()
    {
        foreach (Action release in releases) { release(); }
        Task stop = Runtime.StopAsync();
        WaitUntil(() =>
        {
            Executor.RunAvailable();
            return stop.IsCompleted;
        }, "Test-owned Heartbeat work did not settle during cleanup.");
        await stop.WaitAsync(Deadline);
        await Runtime.DisposeAsync();
    }
}

internal sealed class HeartbeatControlledExecutor : IHeartbeatExecutor
{
    private readonly ConcurrentQueue<Action> pending = new();
    private int posts;

    internal int PendingCount => pending.Count;
    internal int Posts => Volatile.Read(ref posts);

    public bool TryPost(Action start)
    {
        pending.Enqueue(start);
        Interlocked.Increment(ref posts);
        return true;
    }

    internal void RunNext()
    {
        Assert.IsTrue(pending.TryDequeue(out Action? start), "No posted wrapper is available to release.");
        start!();
    }

    internal void RunAvailable()
    {
        while (pending.TryDequeue(out Action? start)) { start(); }
    }
}

internal sealed class HeartbeatTestClock : TimeProvider
{
    private readonly ManualTimeProvider manual = new(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
    private int schedulingChanges;
    private long nextWakeTimestamp = long.MaxValue;

    internal int SchedulingChanges => Volatile.Read(ref schedulingChanges);
    internal long NextWakeTimestamp => Volatile.Read(ref nextWakeTimestamp);
    public override long TimestampFrequency => manual.TimestampFrequency;
    public override long GetTimestamp() => manual.GetTimestamp();
    public override DateTimeOffset GetUtcNow() => manual.GetUtcNow();
    internal void Advance(TimeSpan elapsed) => manual.Advance(elapsed);
    internal void SetUtcNow(DateTimeOffset value) => manual.SetUtcNow(value);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        => new Timer(this, manual.CreateTimer(callback, state, dueTime, period));

    private sealed class Timer(HeartbeatTestClock owner, ITimer actual) : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            bool changed = actual.Change(dueTime, period);
            if (changed)
            {
                Interlocked.Exchange(ref owner.nextWakeTimestamp,
                    dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : checked(owner.GetTimestamp() + dueTime.Ticks));
                Interlocked.Increment(ref owner.schedulingChanges);
            }
            return changed;
        }

        public void Dispose() => actual.Dispose();
        public ValueTask DisposeAsync() => actual.DisposeAsync();
    }
}

internal class HeartbeatProbe : IHeartbeatSubscriber
{
    private int calls, errors;
    internal Func<CancellationToken, Task<bool>> Callback { get; set; } = static _ => Task.FromResult(true);
    internal Func<Exception, Task> Error { get; set; } = static _ => Task.CompletedTask;
    internal int Calls => Volatile.Read(ref calls);
    internal int Errors => Volatile.Read(ref errors);
    internal ConcurrentQueue<CancellationToken> Tokens { get; } = new();

    public Task<bool> OnElapsedAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref calls);
        Tokens.Enqueue(cancellationToken);
        return Callback(cancellationToken);
    }

    public Task OnErrorAsync(Exception exception)
    {
        Interlocked.Increment(ref errors);
        return Error(exception);
    }
}

internal sealed class HeartbeatEqualProbe : HeartbeatProbe
{
    public override bool Equals(object? obj) => obj is HeartbeatEqualProbe;
    public override int GetHashCode() => throw new InvalidOperationException("Subscriber hash codes are not scheduler identity.");
}

internal sealed class HeartbeatRecordingObserver : IHeartbeatObserver
{
    private readonly ConcurrentQueue<HeartbeatObservedFact> facts = new();
    private int flushes, disposals;
    internal Func<ValueTask>? Flush { get; set; }
    internal Func<ValueTask>? Dispose { get; set; }
    internal Action<HeartbeatNotification>? OnReport { get; set; }
    internal int Flushes => Volatile.Read(ref flushes);
    internal int Disposals => Volatile.Read(ref disposals);
    internal HeartbeatObservedFact[] Facts => facts.ToArray();
    internal int Count(HeartbeatEvent kind) => facts.Count(fact => fact.Notification.Kind == kind);
    internal HeartbeatObservedFact Last(HeartbeatEvent kind) => facts.Last(fact => fact.Notification.Kind == kind);

    public void Report(HeartbeatNotification notification)
    {
        OnReport?.Invoke(notification);
        facts.Enqueue(new(notification with
        {
            OriginalError = null,
            ErrorHandlingError = null,
            CleanupError = null,
            IntegrationError = null
        }, notification.OriginalError?.GetType().FullName, notification.ErrorHandlingError?.GetType().FullName,
            notification.CleanupError?.GetType().FullName, notification.IntegrationError?.GetType().FullName));
    }

    public ValueTask FlushAsync()
    {
        Interlocked.Increment(ref flushes);
        return Flush?.Invoke() ?? ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref disposals);
        return Dispose?.Invoke() ?? ValueTask.CompletedTask;
    }
}

internal sealed record HeartbeatObservedFact(HeartbeatNotification Notification, string? OriginalErrorType,
    string? HandlerErrorType, string? CleanupErrorType, string? IntegrationErrorType);

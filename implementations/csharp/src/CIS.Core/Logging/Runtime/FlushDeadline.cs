using System.Runtime.ExceptionServices;

namespace CIS.Logging.Runtime;
// One timer per active caller, with bounded slices so every positive TimeSpan is supported.
// Lock order is deadline gate -> admission gate through observe; no inverse acquisition exists.
internal sealed class FlushDeadline : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly TimeProvider clock;
    private readonly Func<LogFlushResult> observe;
    private readonly Func<LogFlushResult>? observeDeadline;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ITimer timer;
    private long remainingTicks;
    private long checkpoint;
    private bool disposed;
    private LogFlushResult? observation;
    private ExceptionDispatchInfo? error;
    internal FlushDeadline(TimeProvider clock, long started, TimeSpan timeout, Func<LogFlushResult> observe, Func<LogFlushResult>? observeDeadline = null)
    {
        this.clock = clock;
        this.observe = observe;
        this.observeDeadline = observeDeadline;
        remainingTicks = timeout.Ticks;
        checkpoint = started;
        timer = clock.CreateTimer(static state => ((FlushDeadline)state!).Tick(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        Tick();
    }

    internal CancellationToken Token => lifetime.Token;

    private void UpdateRemaining()
    {
        long now = clock.GetTimestamp();
        long elapsed = clock.GetElapsedTime(checkpoint, now).Ticks;
        if (elapsed > 0)
        {
            remainingTicks -= Math.Min(remainingTicks, elapsed);
        }

        checkpoint = now;
    }

    private void Tick()
    {
        bool expire = false;
        lock (gate)
        {
            if (disposed || observation is not null || error is not null)
            {
                return;
            }

            try
            {
                UpdateRemaining();
                if (remainingTicks == 0)
                {
                    observation = (observeDeadline ?? observe)();
                    expire = true;
                }
                else
                {
                    // Timer resolution may overshoot sub-millisecond budgets; never expire early.
                    long delay = Math.Clamp(remainingTicks, TimeSpan.TicksPerMillisecond, TimeSpan.TicksPerDay);
                    timer.Change(TimeSpan.FromTicks(delay), Timeout.InfiniteTimeSpan);
                }
            }
            catch (Exception failure) when (failure is not (OutOfMemoryException or StackOverflowException))
            {
                error = ExceptionDispatchInfo.Capture(failure);
                expire = true;
            }
        }

        // Lifetime cancellation runs outside both gates. A stop observer may already have
        // requested asynchronous output cancellation under deadline -> admission lock order.
        if (expire)
        {
            lifetime.Cancel();
        }
    }

    internal LogFlushResult ObserveCompletion()
    {
        lock (gate)
        {
            error?.Throw();
            // A deadline observation wins over later I/O; do not replace its pending facts.
            if (observation is null)
            {
                UpdateRemaining();
                observation = remainingTicks == 0 ? (observeDeadline ?? observe)() : observe();
            }

            return observation;
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (gate)
        {
            disposed = true;
        }

        // Join an active callback before disposing the token source it may still cancel.
        try
        {
            await timer.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            lifetime.Dispose();
        }
    }
}

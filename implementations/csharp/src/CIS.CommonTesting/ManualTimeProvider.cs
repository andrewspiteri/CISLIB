namespace CIS.CommonTesting;

/// <summary>Controls UTC, monotonic time and timers without sleeping. Advance invokes callbacks synchronously.</summary>
public class ManualTimeProvider : TimeProvider
{
    private readonly object gate = new();
    private readonly object advanceGate = new();
    private readonly List<ManualTimer> timers = [];
    private DateTimeOffset utcNow;
    private long timestamp;
    private bool advancing;

    /// <summary>Starts UTC at the supplied value and monotonic time at zero.</summary>
    public ManualTimeProvider(DateTimeOffset initialUtc) => utcNow = initialUtc.ToUniversalTime();

    /// <inheritdoc />
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        lock (gate)
        {
            return utcNow;
        }
    }

    /// <inheritdoc />
    public override long GetTimestamp()
    {
        lock (gate)
        {
            return timestamp;
        }
    }

    /// <summary>Changes UTC without changing monotonic time or timer eligibility.</summary>
    public void SetUtcNow(DateTimeOffset value)
    {
        lock (gate)
        {
            utcNow = value.ToUniversalTime();
        }
    }

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new ManualTimer(this, callback, state);
        lock (gate)
        {
            timer.Change(dueTime, period);
            timers.Add(timer);
        }

        return timer;
    }

    /// <summary>Advances monotonic time and UTC, invoking due callbacks synchronously without sleeping.</summary>
    public void Advance(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(elapsed.Ticks, nameof(elapsed));
        lock (advanceGate)
        {
            if (advancing)
            {
                throw new InvalidOperationException("Timer callbacks cannot recursively advance time.");
            }
            advancing = true;
            try
            {
                AdvanceTimers(elapsed);
            }
            finally
            {
                advancing = false;
            }
        }
    }

    private void AdvanceTimers(TimeSpan elapsed)
    {
        long target;
        lock (gate)
        {
            target = checked(timestamp + elapsed.Ticks);
        }
        while (true)
        {
            ManualTimer[] ready;
            lock (gate)
            {
                long next = timers.Where(timer => timer.Due is not null).Select(timer => timer.Due!.Value).DefaultIfEmpty(long.MaxValue).Min();
                long destination = Math.Min(next, target);
                utcNow += TimeSpan.FromTicks(destination - timestamp);
                timestamp = destination;
                ready = timers.Where(timer => timer.Due <= timestamp).ToArray();
                foreach (ManualTimer timer in ready)
                {
                    timer.ScheduleNext(timestamp);
                }
                if (ready.Length == 0)
                {
                    return;
                }
            }

            foreach (ManualTimer timer in ready)
            {
                timer.Invoke();
            }
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private long periodTicks;
        private bool disposed;
        internal long? Due
        {
            get; private set;
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Validate(dueTime);
            Validate(period);
            lock (owner.gate)
            {
                if (disposed)
                {
                    return false;
                }
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : checked(owner.timestamp + dueTime.Ticks);
                periodTicks = period.Ticks;
                return true;
            }
        }

        internal void ScheduleNext(long current) => Due = periodTicks > 0 ? checked(current + periodTicks) : null;

        internal void Invoke()
        {
            lock (owner.gate)
            {
                if (disposed)
                {
                    return;
                }
            }
            callback(state);
        }

        public void Dispose()
        {
            lock (owner.gate)
            {
                disposed = true;
                Due = null;
                owner.timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        private static void Validate(TimeSpan value)
        {
            if (value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
        }
    }
}

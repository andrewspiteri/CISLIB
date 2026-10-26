using System.Threading;

namespace CIS.Tests.IntegrationTests.Healthcheck.Reporting;

public sealed partial class HealthcheckPipeTransportTests
{
    private sealed class OwnedTimerActualClock : TimeProvider
    {
        private int created, disposed;
        internal int CreatedTimers => Volatile.Read(ref created);
        internal int DisposedTimers => Volatile.Read(ref disposed);
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => 0;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref created);
            return new ObservedTimer(() => Interlocked.Increment(ref disposed));
        }
        private sealed class ObservedTimer(Action dispose) : ITimer
        {
            private int disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period) => Volatile.Read(ref disposed) == 0;
            public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) { dispose(); } }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}

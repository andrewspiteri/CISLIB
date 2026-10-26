namespace CIS.Tests.IntegrationTests.Healthcheck.Reporting;

public sealed partial class HealthcheckPipeTransportTests
{
    private sealed class ConcurrentClock(ConcurrentFacts facts, ConcurrentTimerFacts counts, Action<int>? beforeDispose = null) : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<ConcurrentTimer> timers = [];
        private int nextIndex;
        public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;
        public override long GetTimestamp() => TimeProvider.System.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref counts.creationEntries);
            try
            {
                var observed = new ConcurrentTimer(facts, counts, callback, state, Interlocked.Increment(ref nextIndex), beforeDispose);
                observed.Publish(TimeProvider.System.CreateTimer(observed.Invoke, null, dueTime, period));
                lock (gate) { timers.Add(observed); }
                Interlocked.Increment(ref counts.created);
                return observed;
            }
            catch (Exception error) { facts.RecordError(error); throw; }
            finally { Interlocked.Increment(ref counts.creationReturns); }
        }
        internal void JoinActualDisposals()
        {
            ConcurrentTimer[] actual; lock (gate) { actual = [.. timers]; }
            var errors = new List<Exception>();
            foreach (ConcurrentTimer timer in actual)
            {
                try { timer.JoinActualAndReleaseObserverReferences(); Interlocked.Increment(ref counts.actualDrainsJoined); }
                catch (Exception error) { errors.Add(error); }
            }
            lock (gate)
            {
                if (timers.Count != actual.Length) { errors.Add(new InvalidOperationException("Actual concurrent timer publication raced its observed cleanup inventory.")); }
            }
            if (errors.Count > 0) { throw new AggregateException("Actual concurrent System timer disposals did not all drain.", errors); }
        }
        internal WeakReference[] WeakTimerOwners() { lock (gate) { return timers.SelectMany(timer => timer.WeakOwners()).ToArray(); } }
        private sealed class ConcurrentTimer(ConcurrentFacts facts, ConcurrentTimerFacts counts, TimerCallback callback, object? state, int index, Action<int>? beforeDispose) : ITimer
        {
            private readonly object gate = new();
            private readonly TaskCompletionSource disposalRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private TimerCallback? observedCallback = callback;
            private object? observedState = state;
            private ITimer? actual;
            private WeakReference? originalActual;
            private Task? disposal;
            private bool disposalStarted;
            internal void Publish(ITimer timer) { lock (gate) { actual = timer; originalActual = new(timer); } }
            internal WeakReference[] WeakOwners() { lock (gate) { return [new(this), originalActual ?? throw new InvalidOperationException("Actual concurrent System timer was never published.")]; } }
            internal void Invoke(object? unused)
            {
                Interlocked.Increment(ref counts.callbacksEntered);
                try
                {
                    TimerCallback current; object? currentState;
                    lock (gate) { current = observedCallback ?? throw new InvalidOperationException("Concurrent callback entered after its actual System drain."); currentState = observedState; }
                    current(currentState);
                }
                catch (Exception error) { facts.RecordError(error); throw; }
                finally { Interlocked.Increment(ref counts.callbacksReturned); }
            }
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                try
                {
                    ITimer timer; lock (gate) { timer = actual ?? throw new ObjectDisposedException(nameof(ConcurrentTimer)); }
                    return timer.Change(dueTime, period);
                }
                catch (Exception error) { facts.RecordError(error); throw; }
            }
            public void Dispose()
            {
                lock (gate) { if (disposalStarted) { return; } disposalStarted = true; }
                Interlocked.Increment(ref counts.disposeEntries);
                try
                {
                    beforeDispose?.Invoke(index);
                    lock (gate) { disposal = (actual ?? throw new InvalidOperationException("Concurrent System timer missing at actual original disposal.")).DisposeAsync().AsTask(); }
                }
                catch (Exception error) { facts.RecordError(error); lock (gate) { disposal = Task.FromException(error); } throw; }
                finally { Interlocked.Increment(ref counts.disposeReturns); disposalRequested.TrySetResult(); }
                // No self-wait: an actual callback may be the current disposer.
            }
            public ValueTask DisposeAsync() { Dispose(); return new(AwaitActualDisposalAsync()); }
            private async Task AwaitActualDisposalAsync()
            {
                await disposalRequested.Task;
                Task drain; lock (gate) { drain = disposal ?? throw new InvalidOperationException("Actual concurrent System disposal promise is absent."); }
                await drain;
            }
            internal void JoinActualAndReleaseObserverReferences()
            {
                JoinConcurrent(disposalRequested.Task);
                Task drain; lock (gate) { drain = disposal ?? throw new InvalidOperationException("Actual concurrent System disposal task missing."); }
                JoinConcurrent(drain);
                lock (gate) { observedCallback = null; observedState = null; actual = null; }
            }
        }
    }
}

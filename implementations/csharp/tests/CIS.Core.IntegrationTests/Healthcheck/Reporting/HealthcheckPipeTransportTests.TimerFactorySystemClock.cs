namespace CIS.Tests.IntegrationTests.Healthcheck.Reporting;

public sealed partial class HealthcheckPipeTransportTests
{
    private sealed class TimerCreationSystemClock(TimerCreationFacts facts) : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<TimerCreationSystemTimer> timers = [];
        public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;
        public override long GetTimestamp() => TimeProvider.System.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            try
            {
                var observed = new TimerCreationSystemTimer(facts, callback, state);
                observed.Publish(TimeProvider.System.CreateTimer(observed.Invoke, null, dueTime, period));
                lock (gate) { timers.Add(observed); }
                Interlocked.Increment(ref facts.createdTimers);
                return observed;
            }
            catch (Exception error) { facts.RecordError(error); throw; }
        }
        internal void JoinActualDisposals()
        {
            TimerCreationSystemTimer[] actual; lock (gate) { actual = [.. timers]; }
            var errors = new List<Exception>();
            foreach (TimerCreationSystemTimer timer in actual)
            {
                try { timer.JoinActualAndReleaseOnlyObserverReferences(); Interlocked.Increment(ref facts.actualDrainsJoined); }
                catch (Exception error) { errors.Add(error); }
            }
            lock (gate)
            {
                if (timers.Count != actual.Length) { throw new InvalidOperationException("Actual replacement request timer publication raced its cleanup inventory."); }
            }
            if (errors.Count > 0) { throw new AggregateException("Actual replacement System request timers did not all drain.", errors); }
        }
        internal WeakReference[] WeakTimerOwners()
        {
            lock (gate) { return timers.SelectMany(timer => timer.WeakOwners()).ToArray(); }
        }
        private sealed class TimerCreationSystemTimer(TimerCreationFacts facts, TimerCallback callback, object? state) : ITimer
        {
            private readonly object gate = new();
            private readonly TaskCompletionSource disposalRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private TimerCallback? observedCallback = callback;
            private object? observedState = state;
            private ITimer? actual;
            private WeakReference? originalActual;
            private Task? actualDisposal;
            internal void Publish(ITimer timer) { lock (gate) { actual = timer; originalActual = new(timer); } }
            internal WeakReference[] WeakOwners() { lock (gate) { return [new(this), originalActual ?? throw new InvalidOperationException("Actual replacement timer owner was never published.")]; } }
            internal void Invoke(object? unused)
            {
                Interlocked.Increment(ref facts.callbacksEntered);
                try
                {
                    TimerCallback current; object? currentState;
                    lock (gate) { current = observedCallback ?? throw new InvalidOperationException("Callback entered after its actual replacement timer drain."); currentState = observedState; }
                    current(currentState);
                }
                catch (Exception error) { facts.RecordError(error); throw; }
                finally { Interlocked.Increment(ref facts.callbacksReturned); }
            }
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                ITimer timer; lock (gate) { timer = actual ?? throw new ObjectDisposedException(nameof(TimerCreationSystemTimer)); }
                try { return timer.Change(dueTime, period); }
                catch (Exception error) { facts.RecordError(error); throw; }
            }
            public void Dispose()
            {
                lock (gate)
                {
                    if (actualDisposal is not null) { return; }
                    try { actualDisposal = (actual ?? throw new InvalidOperationException("Actual replacement System timer not published.")).DisposeAsync().AsTask(); }
                    catch (Exception error) { facts.RecordError(error); actualDisposal = Task.FromException(error); }
                }
                // The real disposal may await this callback. Never self-join.
                disposalRequested.TrySetResult();
            }
            public ValueTask DisposeAsync() { Dispose(); lock (gate) { return new(actualDisposal!); } }
            internal void JoinActualAndReleaseOnlyObserverReferences()
            {
                JoinTimerCreation(disposalRequested.Task);
                Task drain; lock (gate) { drain = actualDisposal ?? throw new InvalidOperationException("Actual replacement System disposal task missing."); }
                JoinTimerCreation(drain);
                lock (gate) { observedCallback = null; observedState = null; actual = null; }
            }
        }
    }
}

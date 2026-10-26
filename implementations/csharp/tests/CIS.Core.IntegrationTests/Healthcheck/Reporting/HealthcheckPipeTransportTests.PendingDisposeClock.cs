namespace CIS.Tests.IntegrationTests.Healthcheck.Reporting;

public sealed partial class HealthcheckPipeTransportTests
{
    private sealed class PendingDisposeTimerFacts
    {
        private readonly object gate = new(); private readonly List<Exception> errors = [];
        internal int createdTimers, disposeRequests, actualDrainsJoined, callbacksEntered, callbacksReturned;
        internal long dueMilliseconds = -1, periodMilliseconds = -2;
        internal void RecordError(Exception error) { lock (gate) { errors.Add(error); } }
        internal Exception[] Errors() { lock (gate) { return [.. errors]; } }
        internal void Require(long due)
        {
            if (Errors().Length != 0) { throw new AggregateException("Actual System timer API/callback/disposal failures remain holds.", Errors()); }
            if (Volatile.Read(ref createdTimers) != 1 || Volatile.Read(ref disposeRequests) != 1 || Volatile.Read(ref actualDrainsJoined) != 1
                || Volatile.Read(ref callbacksEntered) != Volatile.Read(ref callbacksReturned) || Volatile.Read(ref dueMilliseconds) != due || Volatile.Read(ref periodMilliseconds) != -1)
            { throw new InvalidOperationException("The exact actual System timer, original disposal, callback return and separate budget must all settle."); }
        }
    }
    private sealed class PendingDisposeSystemClock(PendingDisposeTimerFacts facts, Action? beforeDispose = null) : TimeProvider
    {
        private readonly object gate = new(); private readonly List<PendingDisposeSystemTimer> timers = [];
        public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;
        public override long GetTimestamp() => TimeProvider.System.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            try
            {
                var timer = new PendingDisposeSystemTimer(facts, callback, state, beforeDispose);
                timer.Publish(TimeProvider.System.CreateTimer(timer.Invoke, null, dueTime, period));
                lock (gate) { timers.Add(timer); }
                Interlocked.Exchange(ref facts.dueMilliseconds, checked((long)dueTime.TotalMilliseconds));
                Interlocked.Exchange(ref facts.periodMilliseconds, checked((long)period.TotalMilliseconds));
                Interlocked.Increment(ref facts.createdTimers);
                return timer;
            }
            catch (Exception error) { facts.RecordError(error); throw; }
        }
        internal void JoinActualDisposals()
        {
            PendingDisposeSystemTimer[] actual; lock (gate) { actual = [.. timers]; }
            var errors = new List<Exception>();
            foreach (PendingDisposeSystemTimer timer in actual)
            {
                try { timer.JoinActualAndReleaseOnlyObserverReferences(); Interlocked.Increment(ref facts.actualDrainsJoined); }
                catch (Exception error) { errors.Add(error); }
            }
            lock (gate) { if (timers.Count != actual.Length) { throw new InvalidOperationException("Actual pending-disposal timer publication raced its cleanup inventory."); } }
            if (errors.Count > 0) { throw new AggregateException("Actual System timer disposals did not all join.", errors); }
        }
        internal WeakReference[] WeakTimerOwners() { lock (gate) { return timers.SelectMany(timer => timer.WeakOwners()).ToArray(); } }
        private sealed class PendingDisposeSystemTimer(PendingDisposeTimerFacts facts, TimerCallback callback, object? state, Action? beforeDispose) : ITimer
        {
            private readonly object gate = new(); private readonly TaskCompletionSource disposalRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private TimerCallback? observedCallback = callback; private object? observedState = state; private ITimer? actual; private WeakReference? originalActual; private Task? actualDisposal;
            internal void Publish(ITimer timer) { lock (gate) { actual = timer; originalActual = new(timer); } }
            internal WeakReference[] WeakOwners() { lock (gate) { return [new(this), originalActual ?? throw new InvalidOperationException("Actual System timer was not published.")]; } }
            internal void Invoke(object? unused)
            {
                Interlocked.Increment(ref facts.callbacksEntered);
                try { TimerCallback current; object? currentState; lock (gate) { current = observedCallback ?? throw new InvalidOperationException("Callback entered after actual System timer drain."); currentState = observedState; } current(currentState); }
                catch (Exception error) { facts.RecordError(error); throw; }
                finally { Interlocked.Increment(ref facts.callbacksReturned); }
            }
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                ITimer timer; lock (gate) { timer = actual ?? throw new ObjectDisposedException(nameof(PendingDisposeSystemTimer)); }
                try { return timer.Change(dueTime, period); } catch (Exception error) { facts.RecordError(error); throw; }
            }
            public void Dispose()
            {
                lock (gate)
                {
                    if (actualDisposal is not null) { return; }
                    try { beforeDispose?.Invoke(); actualDisposal = (actual ?? throw new InvalidOperationException("Actual System timer not published.")).DisposeAsync().AsTask(); Interlocked.Increment(ref facts.disposeRequests); }
                    catch (Exception error) { facts.RecordError(error); actualDisposal = Task.FromException(error); }
                }
                // The real disposal can await the current callback. Never join
                // that task while executing the callback itself.
                disposalRequested.TrySetResult();
            }
            public ValueTask DisposeAsync() { Dispose(); lock (gate) { return new(actualDisposal!); } }
            internal void JoinActualAndReleaseOnlyObserverReferences()
            {
                JoinPendingDispose(disposalRequested.Task); Task task; lock (gate) { task = actualDisposal ?? throw new InvalidOperationException("Actual System disposal task missing."); }
                JoinPendingDispose(task); lock (gate) { observedCallback = null; observedState = null; actual = null; }
            }
        }
    }
}

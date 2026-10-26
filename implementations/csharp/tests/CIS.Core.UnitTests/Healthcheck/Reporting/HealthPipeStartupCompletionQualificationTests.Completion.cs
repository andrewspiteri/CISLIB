using System.Diagnostics;
using System.Runtime.ExceptionServices;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

public sealed partial class HealthPipeStartupCompletionQualificationTests
{
    private static SavedStartObservation ReleaseSavedStartOwners(HealthPipeServer? server, HealthcheckService service,
        CountingSource source, SavedStartBudgetClock clock, HealthcheckOptions options, CancellationTokenSource cancellation,
        Task? exchange, Task? savedStart, Exception? primary)
    {
        Task? stopping = server is null ? null : BeginStop(server);
        Task? disposing = server is null ? null : BeginDispose(server);
        var terminal = new SavedStartTerminal();
        WeakReference<Task>[] observers = server is null ? [] : [ObserveSavedStartTerminal(stopping!, terminal, false), ObserveSavedStartTerminal(disposing!, terminal, true)];
        var holds = new List<Exception>();
        bool phaseJoined = false;
        try
        {
            if (exchange is not null) { JoinSavedStart(exchange); }
            // A pending saved notification is never awaited to manufacture
            // the independent exchange milestone. Already real faults/cancel
            // are observed and retained as cleanup holds.
            if (savedStart is { IsCompleted: true } && !savedStart.IsCompletedSuccessfully) { JoinSavedStart(savedStart); }
            if (server is not null && primary is null && exchange?.IsCompletedSuccessfully == true
                && savedStart?.IsCompletedSuccessfully == true)
            {
                clock.JoinActualRequestDisposal();
                RequireSavedStartPhase(source.Phase, clock.Phase);
                phaseJoined = true;
            }
        }
        catch (Exception error) { holds.Add(error); }
        finally
        {
            try { JoinSavedStart(service.DisposeAsync().AsTask()); }
            catch (Exception error) { holds.Add(error); }
            finally { cancellation.Dispose(); }
        }
        bool candidate = server is not null && primary is null && phaseJoined && holds.Count == 0
            && options.PipeEnabled && options.PipeMaxClients == 1 && options.PipeRequestTimeoutMs == 1000
            && !stopping!.IsFaulted && !stopping.IsCanceled && !disposing!.IsFaulted && !disposing.IsCanceled;
        if (candidate)
        {
            WeakReference[] graph = [new(server!), new(service), new(source), new(cancellation), new(savedStart!), .. clock.WeakGraph()];
            return new(null, graph, source.Phase, clock.Phase, terminal, observers);
        }
        if (server is not null)
        {
            Task all = Task.WhenAll(stopping!, disposing!);
            try { JoinSavedStart(all); }
            catch (Exception error) { holds.Add(all.Exception ?? error); }
            foreach (WeakReference<Task> reference in observers)
            {
                try { if (reference.TryGetTarget(out Task? actualObserver)) { JoinSavedStart(actualObserver); } }
                catch (Exception error) { holds.Add(error); }
            }
            holds.AddRange(terminal.Failures());
        }
        holds.AddRange(source.Phase.Failures());
        if (clock.Phase.CallbackFailure is { } callbackFailure) { holds.Add(callbackFailure); }
        if (holds.Count > 0)
        {
            if (primary is not null) { holds.Insert(0, primary); }
            primary = new AggregateException("The original saved-start assertion or actual exchange/source/timer cleanup did not settle.", holds);
        }
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), [], source.Phase, clock.Phase, terminal, []);
    }

    private static void ReviewSavedStartOwners(SavedStartObservation observed)
    {
        if (observed.Graph.Length != 0)
        {
            var elapsed = Stopwatch.StartNew();
            while (observed.Graph.Any(reference => reference.IsAlive) && elapsed.Elapsed < Deadline)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                if (observed.Graph.Any(reference => reference.IsAlive) && elapsed.Elapsed < Deadline) { Thread.Sleep(1); }
            }
            if (observed.Graph.Any(reference => reference.IsAlive))
            { throw new InvalidOperationException("Actual saved-start slot/source/timer ownership remains live; elapsed time never proves settlement."); }
            RequireSavedStartPhase(observed.Source, observed.Clock);
            foreach (WeakReference<Task> reference in observed.Observers)
            {
                if (reference.TryGetTarget(out Task? actualObserver)) { JoinSavedStart(actualObserver); }
            }
            Exception[] failures = observed.Terminal.Failures();
            if (failures.Length > 0) { throw new AggregateException("Actual saved-start terminal operation failed.", failures); }
            Assert.IsTrue(observed.Terminal.StopCompleted, "Actual Stop must publish success after the real saved-start exchange and slot owner settle.");
            Assert.IsTrue(observed.Terminal.DisposeCompleted, "Actual Dispose must publish success after every actual saved-start scenario resource settles.");
        }
        observed.Primary?.Throw();
    }

    private static void RequireSavedStartPhase(SavedStartSourcePhase source, SavedStartClockPhase clock)
    {
        Exception[] sourceFailures = source.Failures();
        if (sourceFailures.Length > 0) { throw new AggregateException("An unknown actual saved-start source fault remains a runtime hold.", sourceFailures); }
        if (clock.CallbackFailure is { } callbackFailure) { throw new AggregateException("An actual server System timer callback failed.", callbackFailure); }
        if (Volatile.Read(ref source.Captures) != 1 || Volatile.Read(ref source.Exits) != 1
            || Volatile.Read(ref clock.Created) != 1 || Volatile.Read(ref clock.JoinedDisposals) != 1
            || Volatile.Read(ref clock.CallbackEntries) != Volatile.Read(ref clock.CallbackReturns))
        { throw new InvalidOperationException("Exactly one actual saved-start capture/exchange/timer drain is required; unknown phases remain holds."); }
    }
    private static void JoinSavedStart(Task actual) => actual.WaitAsync(Deadline).GetAwaiter().GetResult();
    private static WeakReference<Task> ObserveSavedStartTerminal(Task actual, SavedStartTerminal terminal, bool disposing)
    {
        Task observer = actual.ContinueWith(static (completed, state) =>
        {
            var observed = ((SavedStartTerminal Terminal, bool Disposing))state!;
            if (completed.Exception is { } failure) { observed.Terminal.RecordFailure(failure); }
            else if (completed.IsCanceled) { observed.Terminal.RecordFailure(new TaskCanceledException("Actual saved-start terminal wait canceled.")); }
            else { observed.Terminal.RecordSuccess(observed.Disposing); }
        }, (terminal, disposing), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new(observer);
    }
    private sealed record SavedStartObservation(ExceptionDispatchInfo? Primary, WeakReference[] Graph,
        SavedStartSourcePhase Source, SavedStartClockPhase Clock, SavedStartTerminal Terminal, WeakReference<Task>[] Observers);
    private sealed class SavedStartSourcePhase
    {
        internal int Captures, Exits;
        private readonly object gate = new();
        private readonly List<Exception> faults = [];
        internal void RecordFailure(Exception error) { lock (gate) { faults.Add(error); } }
        internal Exception[] Failures() { lock (gate) { return [.. faults]; } }
    }
    private sealed class SavedStartClockPhase
    {
        internal int Created, JoinedDisposals, CallbackEntries, CallbackReturns;
        private Exception? callbackFailure;
        internal Exception? CallbackFailure => Volatile.Read(ref callbackFailure);
        internal void RecordCallbackFailure(Exception error) => Interlocked.CompareExchange(ref callbackFailure, error, null);
    }
    private sealed class SavedStartTerminal
    {
        private readonly object gate = new();
        private readonly List<Exception> faults = [];
        private bool stop, dispose;
        internal bool StopCompleted { get { lock (gate) { return stop; } } }
        internal bool DisposeCompleted { get { lock (gate) { return dispose; } } }
        internal void RecordFailure(Exception error) { lock (gate) { faults.Add(error); } }
        internal void RecordSuccess(bool disposing) { lock (gate) { if (disposing) { dispose = true; } else { stop = true; } } }
        internal Exception[] Failures() { lock (gate) { return [.. faults]; } }
    }

    private sealed class SavedStartBudgetClock : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<ObservedTimer> timers = [];
        internal SavedStartClockPhase Phase { get; } = new();
        public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;
        public override long GetTimestamp() => TimeProvider.System.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ObservedTimer(this, callback, state);
            timer.Publish(TimeProvider.System.CreateTimer(timer.Invoke, null, dueTime, period));
            lock (gate) { timers.Add(timer); }
            Interlocked.Increment(ref Phase.Created);
            return timer;
        }
        internal void JoinActualRequestDisposal()
        {
            ObservedTimer[] actual; lock (gate) { actual = [.. timers]; }
            if (actual.Length != 1 || Volatile.Read(ref Phase.Created) != 1) { throw new InvalidOperationException("Exactly one actual connected server budget is required."); }
            actual[0].JoinActualDisposal();
            Interlocked.Increment(ref Phase.JoinedDisposals);
        }
        internal WeakReference[] WeakGraph() { lock (gate) { return [new(this), .. timers.Select(timer => new WeakReference(timer))]; } }

        private sealed class ObservedTimer(SavedStartBudgetClock clock, TimerCallback callback, object? state) : ITimer
        {
            private readonly object gate = new();
            private readonly TaskCompletionSource disposalRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private TimerCallback? observedCallback = callback;
            private object? observedState = state;
            private ITimer? actual;
            private Task? actualDisposal;
            internal void Publish(ITimer timer) { lock (gate) { actual = timer; } }
            internal void Invoke(object? unused)
            {
                Interlocked.Increment(ref clock.Phase.CallbackEntries);
                try
                {
                    TimerCallback current; object? currentState;
                    lock (gate) { current = observedCallback ?? throw new InvalidOperationException("Callback entered after actual System disposal drain."); currentState = observedState; }
                    current(currentState);
                }
                catch (Exception error) { clock.Phase.RecordCallbackFailure(error); throw; }
                finally { Interlocked.Increment(ref clock.Phase.CallbackReturns); }
            }
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                ITimer timer; lock (gate) { timer = actual ?? throw new ObjectDisposedException(nameof(ObservedTimer)); }
                return timer.Change(dueTime, period);
            }
            public void Dispose()
            {
                lock (gate)
                {
                    if (actualDisposal is not null) { return; }
                    try { actualDisposal = (actual ?? throw new InvalidOperationException("Actual System timer not published.")).DisposeAsync().AsTask(); }
                    catch (Exception error) { actualDisposal = Task.FromException(error); }
                }
                // A real callback may call Dispose; never self-wait here.
                disposalRequested.TrySetResult();
            }
            public ValueTask DisposeAsync() { Dispose(); lock (gate) { return new(actualDisposal!); } }
            internal void JoinActualDisposal()
            {
                JoinSavedStart(disposalRequested.Task);
                Task drain; lock (gate) { drain = actualDisposal ?? throw new InvalidOperationException("No actual System disposal Task was returned."); }
                JoinSavedStart(drain);
                // Keep the exact actual completed Task for idempotent calls.
                lock (gate) { observedCallback = null; observedState = null; actual = null; }
            }
        }
    }
}

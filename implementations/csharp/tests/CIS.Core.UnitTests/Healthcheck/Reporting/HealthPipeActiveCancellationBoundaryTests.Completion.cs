using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.ExceptionServices;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

public sealed partial class HealthPipeActiveCancellationBoundaryTests
{
    private static void ReviewActualActiveSettlement(ActiveSettlementObservation observed)
    {
        if (observed.Owner is { } owner)
        {
            var clock = Stopwatch.StartNew();
            while (owner.IsAlive && clock.Elapsed < Deadline)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                if (owner.IsAlive && clock.Elapsed < Deadline) { Thread.Sleep(1); }
            }
            if (owner.IsAlive)
            {
                ThrowActiveHold(observed.Primary, new InvalidOperationException("Actual queued/request/IO server ownership remains live after the real source/client/timer owners were joined."));
            }
            try
            {
                RequireActualCompletedRequest(observed.Source!, observed.Clock!);
                foreach (WeakReference<Task> reference in observed.Observers)
                {
                    if (reference.TryGetTarget(out Task? actualObserver)) { JoinActual(actualObserver); }
                }
                Exception[] faults = observed.Status!.Faults();
                if (faults.Length > 0) { throw new AggregateException("Actual terminal operation failed.", faults); }
            }
            catch (Exception hold) { ThrowActiveHold(observed.Primary, hold); }
        }
        observed.Primary?.Throw();
        if (observed.Owner is not null)
        {
            Assert.IsTrue(observed.Status!.StopCompleted, "Actual Stop must publish successful completion after the actual connected source, client IO, request budget and queued server owner settle.");
            Assert.IsTrue(observed.Status.DisposeCompleted, "Actual Dispose must publish successful completion after all actual connected owners settle.");
        }
    }

    // This is called only by the synchronous NoInlining whole-scenario helper.
    // The caller observes weak ownership after BOTH stack frames have returned.
    private static ActiveSettlementObservation ReleaseAndObserveActualActiveOwners(HealthPipeServer? server,
        GatedSource source, ObservedBudgetClock clock, HealthcheckService service, ManualResetEventSlim release,
        NamedPipeClientStream? client, Task<int>? reading, Task? terminal, Exception? primary)
    {
        Task? stopping = server is null ? null : BeginStop(server);
        Task? disposing = server is null ? null : BeginDispose(server);
        var status = new ActiveTerminalStatus();
        var actualObservers = new List<WeakReference<Task>>();
        if (server is not null)
        {
            actualObservers.Add(ObserveActiveTerminal(stopping!, status, false));
            actualObservers.Add(ObserveActiveTerminal(disposing!, status, true));
        }
        if (terminal is not null) { actualObservers.Add(ObserveOriginalTerminal(terminal, status)); }
        WeakReference<Task>[] observers = [.. actualObservers];
        var holds = new List<Exception>();
        bool actualRequestJoined = false;
        try
        {
            release.Set();
            client?.Dispose();
            if (reading is not null)
            {
                try { _ = JoinActual(reading); }
                catch (Exception error) when (error is IOException or ObjectDisposedException) { }
            }
            if (server is not null && source.Entered.IsCompletedSuccessfully)
            {
                JoinActual(source.Returned);
                clock.JoinActualBudgetDisposal();
                RequireActualCompletedRequest(source, clock);
                actualRequestJoined = true;
            }
        }
        catch (Exception error) { holds.Add(error); }
        finally
        {
            try { JoinActual(service.DisposeAsync().AsTask()); }
            catch (Exception error) { holds.Add(error); }
        }
        bool candidate = server is not null && actualRequestJoined && holds.Count == 0
            && !stopping!.IsFaulted && !stopping.IsCanceled && !disposing!.IsFaulted && !disposing.IsCanceled;
        if (candidate)
        {
            // All actual source/client/budget callbacks were joined above. For
            // this reviewed one-slot accounting family, actual BeginStop closes
            // admission/stream; RunSlot retains its server through remaining
            // response work, real finally and final progress notification.
            // A weak owner can therefore outlive the resource joins, but cannot
            // be excused while actual request/queued work still owns it.
            return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), new(server), source, clock, status, observers);
        }
        if (server is not null)
        {
            Task drains = terminal is null ? Task.WhenAll(stopping!, disposing!) : Task.WhenAll(stopping!, disposing!, terminal);
            try { JoinActual(drains); }
            catch (Exception error) { holds.Add(drains.Exception ?? error); }
            foreach (WeakReference<Task> reference in observers)
            {
                try { if (reference.TryGetTarget(out Task? actualObserver)) { JoinActual(actualObserver); } }
                catch (Exception error) { holds.Add(error); }
            }
            holds.AddRange(status.Faults());
        }
        if (holds.Count > 0)
        {
            if (primary is not null) { holds.Insert(0, primary); }
            primary = new AggregateException("The actual connected scenario or owned cleanup did not settle successfully.", holds);
        }
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), null, null, null, null, []);
    }

    private static void RequireActualCompletedRequest(GatedSource source, ObservedBudgetClock clock)
    {
        if (source.ActualFailure is { } actualSourceFailure)
        {
            throw new AggregateException("The actual source invocation failed; its fault remains a runtime cleanup hold.", actualSourceFailure);
        }
        if (clock.ActualCallbackFailure is { } actualCallbackFailure)
        {
            throw new AggregateException("The actual System timer callback failed; its fault remains a runtime cleanup hold.", actualCallbackFailure);
        }
        if (source.Captures != 1 || source.Returns != 1 || !source.Returned.IsCompletedSuccessfully
            || clock.Created != 1 || clock.ActualDrainsJoined != 1 || clock.CallbacksEntered != clock.CallbacksReturned)
        {
            throw new InvalidOperationException("The actual source or System request-timer/callback phase is unresolved; managed drain accounting cannot excuse it.");
        }
    }

    private static void ThrowActiveHold(ExceptionDispatchInfo? primary, Exception hold)
    {
        if (primary is not null) { throw new AggregateException("The receiving body and actual owned cleanup both failed.", primary.SourceException, hold); }
        ExceptionDispatchInfo.Capture(hold).Throw();
    }

    private static void JoinActual(Task actual) => actual.WaitAsync(Deadline).GetAwaiter().GetResult();
    private static T JoinActual<T>(Task<T> actual) => actual.WaitAsync(Deadline).GetAwaiter().GetResult();
    private static WeakReference<Task> ObserveActiveTerminal(Task actual, ActiveTerminalStatus status, bool isDispose)
    {
        Task observer = actual.ContinueWith(static (completed, state) =>
        {
            var observed = ((ActiveTerminalStatus Status, bool IsDispose))state!;
            if (completed.Exception is { } fault) { observed.Status.RecordFault(fault); }
            else if (completed.IsCanceled) { observed.Status.RecordFault(new TaskCanceledException("Actual terminal wait canceled.")); }
            else { observed.Status.RecordSuccess(observed.IsDispose); }
        }, (status, isDispose), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new(observer);
    }

    private static WeakReference<Task> ObserveOriginalTerminal(Task actual, ActiveTerminalStatus status)
    {
        Task observer = actual.ContinueWith(static (completed, state) =>
        {
            var observed = (ActiveTerminalStatus)state!;
            if (completed.Exception is { } fault) { observed.RecordFault(fault); }
            else if (completed.IsCanceled) { observed.RecordFault(new TaskCanceledException("Original actual terminal wait canceled.")); }
        }, status, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new(observer);
    }

    private sealed record ActiveSettlementObservation(ExceptionDispatchInfo? Primary, WeakReference? Owner,
        GatedSource? Source, ObservedBudgetClock? Clock, ActiveTerminalStatus? Status, WeakReference<Task>[] Observers);

    private sealed class ActiveTerminalStatus
    {
        private readonly object gate = new();
        private readonly List<Exception> faults = [];
        private bool stop, dispose;
        internal bool StopCompleted { get { lock (gate) { return stop; } } }
        internal bool DisposeCompleted { get { lock (gate) { return dispose; } } }
        internal void RecordSuccess(bool isDispose) { lock (gate) { if (isDispose) { dispose = true; } else { stop = true; } } }
        internal void RecordFault(Exception error) { lock (gate) { faults.Add(error); } }
        internal Exception[] Faults() { lock (gate) { return [.. faults]; } }
    }

    // Time and deadlines are real System operations. The only extra ownership
    // is the fixture's actual timer DisposeAsync join, requested without waiting
    // inside Dispose so an in-flight timer callback cannot self-wait.
    private sealed class ObservedBudgetClock : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<ObservedBudgetTimer> timers = [];
        private int created, actualDrainsJoined, callbacksEntered, callbacksReturned;
        private Exception? actualCallbackFailure;
        internal int Created => Volatile.Read(ref created);
        internal int ActualDrainsJoined => Volatile.Read(ref actualDrainsJoined);
        internal int CallbacksEntered => Volatile.Read(ref callbacksEntered);
        internal int CallbacksReturned => Volatile.Read(ref callbacksReturned);
        internal Exception? ActualCallbackFailure => Volatile.Read(ref actualCallbackFailure);
        public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;
        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow();
        public override long GetTimestamp() => TimeProvider.System.GetTimestamp();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ObservedBudgetTimer(this, callback, state);
            timer.PublishActual(TimeProvider.System.CreateTimer(timer.InvokeActualCallback, null, dueTime, period));
            lock (gate) { timers.Add(timer); }
            Interlocked.Increment(ref created);
            return timer;
        }
        internal void JoinActualBudgetDisposal()
        {
            ObservedBudgetTimer[] actual;
            lock (gate) { actual = [.. timers]; }
            if (actual.Length != 1 || Created != 1) { throw new InvalidOperationException("Exactly one real request budget is required for this joined-source phase."); }
            actual[0].JoinActualDisposalAndReleaseObserverReferences();
            Interlocked.Increment(ref actualDrainsJoined);
        }

        private sealed class ObservedBudgetTimer(ObservedBudgetClock clock, TimerCallback callback, object? state) : ITimer
        {
            private readonly object gate = new();
            private readonly TaskCompletionSource disposalRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private TimerCallback? observedCallback = callback;
            private object? observedState = state;
            private ITimer? actual;
            private Task? actualDisposal;
            internal void PublishActual(ITimer timer) { lock (gate) { actual = timer; } }
            internal void InvokeActualCallback(object? unused)
            {
                Interlocked.Increment(ref clock.callbacksEntered);
                try
                {
                    TimerCallback current; object? currentState;
                    lock (gate) { current = observedCallback ?? throw new InvalidOperationException("An actual callback ran after its real timer drain."); currentState = observedState; }
                    current(currentState);
                }
                catch (Exception error)
                {
                    Interlocked.CompareExchange(ref clock.actualCallbackFailure, error, null);
                    throw;
                }
                finally { Interlocked.Increment(ref clock.callbacksReturned); }
            }
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                ITimer timer; lock (gate) { timer = actual ?? throw new ObjectDisposedException(nameof(ObservedBudgetTimer)); }
                return timer.Change(dueTime, period);
            }
            public void Dispose()
            {
                lock (gate)
                {
                    if (actualDisposal is not null) { return; }
                    try { actualDisposal = (actual ?? throw new InvalidOperationException("Actual System timer was not published.")).DisposeAsync().AsTask(); }
                    catch (Exception error) { actualDisposal = Task.FromException(error); }
                }
                disposalRequested.TrySetResult();
            }
            public ValueTask DisposeAsync() { Dispose(); lock (gate) { return new(actualDisposal!); } }
            internal void JoinActualDisposalAndReleaseObserverReferences()
            {
                JoinActual(disposalRequested.Task);
                Task drain; lock (gate) { drain = actualDisposal ?? throw new InvalidOperationException("No actual timer disposal Task was returned."); }
                JoinActual(drain);
                // Retain the exact joined actual disposal Task for idempotent
                // repeated Dispose/DisposeAsync. Release only observer-owned
                // delegate/state/timer references after that real drain.
                lock (gate) { observedCallback = null; observedState = null; actual = null; }
            }
        }
    }
}

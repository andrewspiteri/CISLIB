using System.Diagnostics;
using System.Runtime.ExceptionServices;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

public sealed partial class HealthPipeReceivingBoundaryQualificationTests
{
    private enum ReceivingScenario { SourceError, InvalidCli, Deadline }

    private static ReceivingObservation ReleaseReceivingOwners(HealthPipeServer? server, HealthcheckService service,
        SnapshotSource source, ReceivingClock clock, HealthcheckOptions options, CancellationTokenSource cancellation,
        List<Task> actualClients, ReceivingScenario scenario, Exception? primary)
    {
        Task? stopping = server is null ? null : BeginStop(server);
        Task? disposing = server is null ? null : BeginDispose(server);
        var terminal = new ReceivingTerminal();
        WeakReference<Task>[] observers = server is null ? [] : [ObserveReceivingTerminal(stopping!, terminal, false), ObserveReceivingTerminal(disposing!, terminal, true)];
        var holds = new List<Exception>();
        bool phaseJoined = false;
        // Each retained Task owns the original client IO/stream scope. The
        // rejected CLI task intentionally performs no endpoint exchange.
        foreach (Task actual in actualClients)
        {
            try { JoinReceiving(actual); }
            catch (Exception error) { holds.Add(error); }
        }
        try
        {
            if (server is not null && primary is null && holds.Count == 0
                && actualClients.Count == 2 && actualClients.All(actual => actual.IsCompletedSuccessfully))
            {
                clock.JoinActualRequestDisposals(scenario == ReceivingScenario.InvalidCli ? 1 : 2);
                RequireReceivingPhase(source.Phase, clock.Phase, scenario);
                phaseJoined = true;
            }
        }
        catch (Exception error) { holds.Add(error); }
        finally
        {
            try { JoinReceiving(service.DisposeAsync().AsTask()); }
            catch (Exception error) { holds.Add(error); }
            finally { cancellation.Dispose(); }
        }
        bool candidate = server is not null && primary is null && phaseJoined && holds.Count == 0
            && options.PipeEnabled && options.PipeMaxClients == 1 && options.PipeRequestTimeoutMs == 1000
            && options.DetailsEnabled && options.MaxReportBytes == 1024
            && !stopping!.IsFaulted && !stopping.IsCanceled && !disposing!.IsFaulted && !disposing.IsCanceled;
        if (candidate)
        {
            WeakReference[] graph = [new(server!), new(service), new(source), new(cancellation), .. clock.WeakGraph()];
            return new(null, graph, source.Phase, clock.Phase, scenario, terminal, observers);
        }
        if (server is not null)
        {
            Task all = Task.WhenAll(stopping!, disposing!);
            try { JoinReceiving(all); }
            catch (Exception error) { holds.Add(all.Exception ?? error); }
            foreach (WeakReference<Task> reference in observers)
            {
                try { if (reference.TryGetTarget(out Task? actualObserver)) { JoinReceiving(actualObserver); } }
                catch (Exception error) { holds.Add(error); }
            }
            holds.AddRange(terminal.Failures());
        }
        holds.AddRange(source.Phase.UnexpectedFailures(scenario == ReceivingScenario.SourceError));
        holds.AddRange(clock.Phase.Failures());
        if (holds.Count > 0)
        {
            if (primary is not null) { holds.Insert(0, primary); }
            primary = new AggregateException("The original receiving assertions or actual client/source/timer cleanup did not settle.", holds);
        }
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), [], source.Phase, clock.Phase, scenario, terminal, []);
    }

    private static void ReviewReceivingOwners(ReceivingObservation observed)
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
            { throw new InvalidOperationException("Actual receiving IO/source/timer/queued owner remains live; elapsed time never certifies settlement."); }
            RequireReceivingPhase(observed.Source, observed.Clock, observed.Scenario);
            foreach (WeakReference<Task> reference in observed.Observers)
            {
                if (reference.TryGetTarget(out Task? actualObserver)) { JoinReceiving(actualObserver); }
            }
            Exception[] failures = observed.Terminal.Failures();
            if (failures.Length > 0) { throw new AggregateException("An actual receiving terminal operation failed.", failures); }
            Assert.IsTrue(observed.Terminal.StopCompleted, "Actual Stop must publish success after the original receiving scenario and actual slot owners settle.");
            Assert.IsTrue(observed.Terminal.DisposeCompleted, "Actual Dispose must publish success after every original receiving resource settles.");
        }
        observed.Primary?.Throw();
    }

    private static void RequireReceivingPhase(ReceivingSourcePhase source, ReceivingClockPhase clock, ReceivingScenario scenario)
    {
        bool originalSourceError = scenario == ReceivingScenario.SourceError;
        Exception[] failures = [.. source.UnexpectedFailures(originalSourceError), .. clock.Failures()];
        if (failures.Length > 0) { throw new AggregateException("An unknown actual source or clock callback failure remains a runtime cleanup hold.", failures); }
        int captures = originalSourceError ? 2 : 1, timers = scenario == ReceivingScenario.InvalidCli ? 1 : 2;
        if (Volatile.Read(ref source.Captures) != captures || Volatile.Read(ref source.Exits) != captures
            || !source.ExactExpectedPhase(originalSourceError)
            || Volatile.Read(ref clock.Created) != timers || Volatile.Read(ref clock.JoinedDisposals) != timers
            || Volatile.Read(ref clock.CallbackEntries) != Volatile.Read(ref clock.CallbackReturns))
        { throw new InvalidOperationException("Only the original completed receiving captures and actual timer drains can release the owner; extra or raced phases remain holds."); }
        bool manual = scenario == ReceivingScenario.Deadline;
        if (clock.Manual != manual || (manual
                ? Volatile.Read(ref clock.Advances) != 1 || Volatile.Read(ref clock.AdvancedMilliseconds) != 1000
                    || Volatile.Read(ref clock.CallbackEntries) != 0
                : Volatile.Read(ref clock.Advances) != 0 || Volatile.Read(ref clock.AdvancedMilliseconds) != 0))
        { throw new InvalidOperationException("The original deadline Advance and intentionally delayed-callback phase must remain exact."); }
        ReceivingTimerPhase[] actualTimers = clock.TimerPhases();
        if (actualTimers.Length != timers || actualTimers.Any(timer => Volatile.Read(ref timer.Requests) <= 0 || Volatile.Read(ref timer.Returns) <= 0))
        { throw new InvalidOperationException("Every actual created receiving timer must receive and complete disposal."); }
    }

    private static void JoinReceiving(Task actual) => actual.WaitAsync(Deadline).GetAwaiter().GetResult();
    private static T JoinReceiving<T>(Task<T> actual) => actual.WaitAsync(Deadline).GetAwaiter().GetResult();
    private static WeakReference<Task> ObserveReceivingTerminal(Task actual, ReceivingTerminal terminal, bool disposing)
    {
        Task observer = actual.ContinueWith(static (completed, state) =>
        {
            var observed = ((ReceivingTerminal Terminal, bool Disposing))state!;
            if (completed.Exception is { } failure) { observed.Terminal.RecordFailure(failure); }
            else if (completed.IsCanceled) { observed.Terminal.RecordFailure(new TaskCanceledException("Actual receiving terminal wait canceled.")); }
            else { observed.Terminal.RecordSuccess(observed.Disposing); }
        }, (terminal, disposing), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new(observer);
    }
    private sealed record ReceivingObservation(ExceptionDispatchInfo? Primary, WeakReference[] Graph,
        ReceivingSourcePhase Source, ReceivingClockPhase Clock, ReceivingScenario Scenario, ReceivingTerminal Terminal, WeakReference<Task>[] Observers);
    private sealed class ReceivingSourcePhase
    {
        internal int Captures, Exits;
        private readonly object gate = new();
        private readonly List<Exception> faults = [];
        private InvalidOperationException? produced;
        private int producedCount, producedAtCapture;
        internal void RecordProduced(InvalidOperationException actual, int capture)
        { lock (gate) { produced = actual; producedCount++; producedAtCapture = capture; } }
        internal void RecordFailure(Exception error) { lock (gate) { faults.Add(error); } }
        internal bool ExactExpectedPhase(bool expected)
        {
            lock (gate)
            {
                return expected ? producedCount == 1 && producedAtCapture == 1
                    && produced?.GetType() == typeof(InvalidOperationException) && produced.Message == "private-source-detail"
                    && faults.Count == 1 && ReferenceEquals(faults[0], produced)
                    : producedCount == 0 && faults.Count == 0;
            }
        }
        internal Exception[] UnexpectedFailures(bool expected)
        {
            lock (gate)
            {
                bool original = expected && producedCount == 1 && producedAtCapture == 1
                    && produced?.GetType() == typeof(InvalidOperationException) && produced.Message == "private-source-detail";
                return faults.Where(error => !original || !ReferenceEquals(error, produced)).ToArray();
            }
        }
    }
    private sealed class ReceivingTimerPhase
    {
        internal int Requests, Returns;
    }
    private sealed class ReceivingClockPhase(bool manual)
    {
        internal bool Manual { get; } = manual;
        internal int Created, JoinedDisposals, CallbackEntries, CallbackReturns, Advances;
        internal long AdvancedMilliseconds;
        private readonly object gate = new();
        private readonly List<Exception> faults = [];
        private readonly List<ReceivingTimerPhase> timers = [];
        internal void RecordTimer(ReceivingTimerPhase phase) { lock (gate) { timers.Add(phase); } }
        internal ReceivingTimerPhase[] TimerPhases() { lock (gate) { return [.. timers]; } }
        internal void RecordFailure(Exception error) { lock (gate) { faults.Add(error); } }
        internal Exception[] Failures() { lock (gate) { return [.. faults]; } }
    }
    private sealed class ReceivingTerminal
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

    // Only the two existing clock kinds share this fixture-local accounting.
    private abstract class ReceivingClock(bool manual) : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<ReceivingTimer> timers = [];
        internal ReceivingClockPhase Phase { get; } = new(manual);
        internal void RecordActualTimer(ReceivingTimer timer)
        {
            lock (gate) { timers.Add(timer); }
            Phase.RecordTimer(timer.Phase);
            Interlocked.Increment(ref Phase.Created);
        }
        internal void JoinActualRequestDisposals(int expected)
        {
            ReceivingTimer[] actual; lock (gate) { actual = [.. timers]; }
            if (actual.Length != expected || Volatile.Read(ref Phase.Created) != expected)
            { throw new InvalidOperationException("The exact number of actual server request timers must be present."); }
            foreach (ReceivingTimer timer in actual)
            {
                timer.JoinActualDisposal();
                Interlocked.Increment(ref Phase.JoinedDisposals);
            }
        }
        internal WeakReference[] WeakGraph() { lock (gate) { return [new(this), .. timers.Select(timer => new WeakReference(timer))]; } }
    }
    private abstract class ReceivingTimer : ITimer
    {
        internal ReceivingTimerPhase Phase { get; } = new();
        protected TaskCompletionSource Returned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected ref int DisposalRequests => ref Phase.Requests;
        protected ref int DisposalReturns => ref Phase.Returns;
        public abstract bool Change(TimeSpan dueTime, TimeSpan period);
        public abstract void Dispose();
        public abstract ValueTask DisposeAsync();
        internal abstract void JoinActualDisposal();
    }
    private sealed class ReceivingSystemClock() : ReceivingClock(manual: false)
    {
        public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;
        public override long GetTimestamp() => TimeProvider.System.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ObservedSystemTimer(this, callback, state);
            timer.Publish(TimeProvider.System.CreateTimer(timer.Invoke, null, dueTime, period));
            RecordActualTimer(timer);
            return timer;
        }
        private sealed class ObservedSystemTimer(ReceivingSystemClock clock, TimerCallback callback, object? state) : ReceivingTimer
        {
            private readonly object gate = new();
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
                catch (Exception error) { clock.Phase.RecordFailure(error); throw; }
                finally { Interlocked.Increment(ref clock.Phase.CallbackReturns); }
            }
            public override bool Change(TimeSpan dueTime, TimeSpan period)
            {
                ITimer timer; lock (gate) { timer = actual ?? throw new ObjectDisposedException(nameof(ObservedSystemTimer)); }
                return timer.Change(dueTime, period);
            }
            public override void Dispose()
            {
                Interlocked.Increment(ref DisposalRequests);
                lock (gate)
                {
                    if (actualDisposal is null)
                    {
                        try { actualDisposal = (actual ?? throw new InvalidOperationException("Actual System timer not published.")).DisposeAsync().AsTask(); }
                        catch (Exception error) { actualDisposal = Task.FromException(error); }
                    }
                }
                Interlocked.Increment(ref DisposalReturns);
                Returned.TrySetResult();
            }
            public override ValueTask DisposeAsync() { Dispose(); lock (gate) { return new(actualDisposal!); } }
            internal override void JoinActualDisposal()
            {
                JoinReceiving(Returned.Task);
                Task drain; lock (gate) { drain = actualDisposal ?? throw new InvalidOperationException("No actual System disposal Task was returned."); }
                JoinReceiving(drain);
                lock (gate) { observedCallback = null; observedState = null; actual = null; }
            }
        }
    }
}

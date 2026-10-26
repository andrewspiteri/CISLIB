using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Healthcheck.Reporting;

public sealed partial class HealthcheckProbeProcessTests
{
    private static readonly TimeSpan ProbeProcessDeadline = TransportTestSupport.Limit;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ProbeProcessObservation RunProbeProcessScenario(string view, bool unhealthy, bool startup, bool details, int exit,
        HealthcheckService? observedService = null, IConditionMonitor? observedMonitor = null)
    {
        if ((observedService is null) != (observedMonitor is null)) { throw new ArgumentException("Both actual control owners must be supplied together."); }
        string pipe = TransportTestSupport.NewPipeName();
        var options = new HealthcheckOptions { PipeEnabled = true, PipeName = pipe, DetailsEnabled = details };
        var service = observedService ?? new HealthcheckService(options, new HealthApplicationIdentity { Name = "probe-fixture", InstanceId = Guid.NewGuid() });
        IConditionMonitor monitor = observedMonitor ?? service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = unhealthy });
        if (observedService is null && startup) { service.MarkStartupCompleted(); }
        var facts = new ProbeProcessFacts { expectedCaptures = details || view != "details" ? 1 : 0 };
        var source = new ProbeProcessSource(service, facts);
        var clock = new ProbeProcessClock(facts);
        HealthPipeServer? server = null;
        Probe? probe = null;
        Task<(int Exit, string Output, string Error)>? finish = null;
        Exception? primary = null;
        try
        {
            server = new HealthPipeServer(source, options, clock);
            JoinProbeProcess(TransportTestSupport.StartServerAsync(server, pipe));
            probe = new Probe(["--pipe", pipe, "--view", view, "--timeout-ms", "3000"], captureExit: true);
            finish = probe.Finish();
            var result = JoinProbeProcess(finish);
            Assert.AreEqual(exit, result.Exit, result.Error);
            if (exit == 2) { ErrorResult(result, "DetailsDisabled"); }
            else { TransportTestSupport.ValidateSnapshot(JsonNode.Parse(result.Output)!.AsObject(), view, !unhealthy, startup && !unhealthy); Assert.AreEqual("", result.Error); }
            Volatile.Write(ref facts.originalAssertionsPassed, 1);
        }
        catch (Exception error) { primary = error; }
        return ReleaseProbeProcessOwners(server, service, monitor, source, clock, facts, probe, finish, primary);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ProbeProcessObservation ReleaseProbeProcessOwners(HealthPipeServer? server,
        HealthcheckService service, IConditionMonitor monitor, ProbeProcessSource source, ProbeProcessClock clock,
        ProbeProcessFacts facts, Probe? probe, Task<(int Exit, string Output, string Error)>? finish, Exception? primary)
    {
        var holds = new List<Exception>();
        Process? child = probe?.ActualChild;
        Task? exit = probe?.ActualExit;
        Task<string>? stdout = probe?.ActualOutput, stderr = probe?.ActualError;
        Task? probeDisposal = null;
        if (probe is not null)
        {
            try
            {
                if (finish?.IsCompletedSuccessfully == true && exit?.IsCompletedSuccessfully == true && child!.HasExited)
                {
                    Volatile.Write(ref facts.naturalExitBeforeCleanup, 1);
                }
                facts.actualPid = child!.Id;
            }
            catch (Exception error) { holds.Add(error); }
            try { probeDisposal = probe.DisposeAsync().AsTask(); JoinProbeProcess(probeDisposal); Volatile.Write(ref facts.probeDisposalJoined, 1); }
            catch (Exception error) { holds.Add(error); }
        }
        // Preserve the original child-before-server disposal order. Observe the
        // actual exit and both EOF tasks independently of Finish and Process.Dispose.
        foreach ((Task? actual, int kind) in new (Task?, int)[] { (finish, 0), (exit, 1), (stdout, 2), (stderr, 3) })
        {
            if (actual is null) { continue; }
            try { JoinProbeProcess(actual); facts.RecordChildJoin(kind); }
            catch (Exception error) when (ReferenceEquals(actual, finish) && ReferenceEquals(error, primary))
            {
                // Re-observe only the same completed Finish's exact saved error;
                // keep it as the body failure and never make it eligible.
                Volatile.Write(ref facts.finishObserved, 1);
            }
            catch (Exception error) { holds.Add(error); }
        }
        Task? stopping = server is null ? null : BeginProbeProcessStop(server);
        Task? disposing = server is null ? null : BeginProbeProcessDispose(server);
        var status = new ProbeProcessStatus();
        var observers = new List<WeakReference<Task>>();
        if (stopping is not null) { observers.Add(ObserveProbeProcessTerminal(stopping, status, false)); }
        if (disposing is not null) { observers.Add(ObserveProbeProcessTerminal(disposing, status, true)); }
        try { clock.JoinActualDisposals(); }
        catch (Exception error) { holds.Add(error); }
        try { JoinProbeProcess(monitor.DisposeAsync().AsTask()); Volatile.Write(ref facts.monitorJoined, 1); }
        catch (Exception error) { holds.Add(error); }
        try { JoinProbeProcess(service.DisposeAsync().AsTask()); Volatile.Write(ref facts.serviceJoined, 1); }
        catch (Exception error) { holds.Add(error); }
        GC.KeepAlive(monitor);
        // A real source error may have become ReportingUnavailable and then the
        // original child-result assertion. Preserve both actual causes always.
        holds.AddRange(facts.Errors());
        bool eligible = primary is null && holds.Count == 0 && server is not null && probe is not null && child is not null
            && finish is not null && exit is not null && stdout is not null && stderr is not null && probeDisposal is not null
            && !stopping!.IsFaulted && !stopping.IsCanceled && !disposing!.IsFaulted && !disposing.IsCanceled;
        if (eligible)
        {
            try { RequireProbeProcessPhase(facts); }
            catch (Exception error) { holds.Add(error); eligible = false; }
        }
        if (eligible)
        {
            // Exact remaining-decrement leaves only: real BeginStop still closes
            // all eight streams. Child IO/process lifetime, source, System timer
            // drains and state disposal have joined before releasing these owners.
            // Completed public tasks may be runtime singletons; their real joins,
            // not collection of Task.CompletedTask, certify their terminal state.
            WeakReference[] graph = [new(server!), new(service), new(monitor), new(source), new(clock), new(probe!), new(child!), .. clock.WeakTimerOwners()];
            return new(null, graph, facts, status, [.. observers]);
        }
        if (server is not null)
        {
            Task all = Task.WhenAll(stopping!, disposing!);
            try { JoinProbeProcess(all); }
            catch (Exception error) { holds.Add(all.Exception ?? error); }
            foreach (WeakReference<Task> observer in observers)
            {
                try { if (observer.TryGetTarget(out Task? actual)) { JoinProbeProcess(actual); } }
                catch (Exception error) { holds.Add(error); }
            }
            holds.AddRange(status.Errors());
        }
        if (holds.Count > 0)
        {
            if (primary is not null) { holds.Insert(0, primary); }
            primary = new AggregateException("The original real probe body or its actual child/server cleanup did not settle.", holds);
        }
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), [], facts, null, []);
    }

    private static void ReviewProbeProcessScenario(ProbeProcessObservation observed)
    {
        if (observed.Graph.Length > 0)
        {
            var elapsed = Stopwatch.StartNew();
            while (observed.Graph.Any(reference => reference.IsAlive) && elapsed.Elapsed < ProbeProcessDeadline)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                if (observed.Graph.Any(reference => reference.IsAlive) && elapsed.Elapsed < ProbeProcessDeadline) { Thread.Sleep(1); }
            }
            if (observed.Graph.Any(reference => reference.IsAlive)) { throw new InvalidOperationException("Actual probe process/server/source/clock/timer ownership remains live; elapsed time cannot certify settlement."); }
            RequireProbeProcessPhase(observed.Facts);
            foreach (WeakReference<Task> reference in observed.Observers) { if (reference.TryGetTarget(out Task? actual)) { JoinProbeProcess(actual); } }
            Exception[] errors = observed.Status!.Errors();
            if (errors.Length > 0) { throw new AggregateException("An actual uncanceled terminal operation failed.", errors); }
        }
        observed.Primary?.Throw();
        if (observed.Graph.Length > 0)
        {
            Assert.IsTrue(observed.Status!.StopCompleted, "Actual terminal Stop must publish success after the natural child exit, complete output, real timer and all eight queued workers settle.");
            Assert.IsTrue(observed.Status.DisposeCompleted, "Actual terminal Dispose must publish success after the natural child exit, complete output, real timer and all eight queued workers settle.");
        }
    }

    private static void RequireProbeProcessPhase(ProbeProcessFacts facts)
    {
        Exception[] errors = facts.Errors();
        if (errors.Length > 0) { throw new AggregateException("An actual source, clock or timer callback error remains a resource hold.", errors); }
        if (facts.expectedCaptures is not (0 or 1) || Volatile.Read(ref facts.captures) != facts.expectedCaptures || Volatile.Read(ref facts.returns) != facts.expectedCaptures
            || Volatile.Read(ref facts.createdTimers) != 1 || Volatile.Read(ref facts.actualDrainsJoined) != 1
            || Volatile.Read(ref facts.callbacksEntered) != Volatile.Read(ref facts.callbacksReturned)
            || Volatile.Read(ref facts.originalAssertionsPassed) != 1 || Volatile.Read(ref facts.naturalExitBeforeCleanup) != 1 || facts.actualPid <= 0
            || Volatile.Read(ref facts.probeDisposalJoined) != 1 || Volatile.Read(ref facts.finishObserved) != 1 || Volatile.Read(ref facts.exitJoined) != 1
            || Volatile.Read(ref facts.stdoutJoined) != 1 || Volatile.Read(ref facts.stderrJoined) != 1
            || Volatile.Read(ref facts.monitorJoined) != 1 || Volatile.Read(ref facts.serviceJoined) != 1)
        {
            throw new InvalidOperationException("Exact original natural child result, Finish/exit/stdout/stderr/Probe lifecycle joins, expected source phase, one actual System timer drain and real state joins are required; unknown phases remain holds.");
        }
    }
    private static void JoinProbeProcess(Task actual) => actual.WaitAsync(ProbeProcessDeadline).GetAwaiter().GetResult();
    private static T JoinProbeProcess<T>(Task<T> actual) => actual.WaitAsync(ProbeProcessDeadline).GetAwaiter().GetResult();
    private static Task BeginProbeProcessStop(HealthPipeServer server) { try { return server.StopAsync(); } catch (Exception error) { return Task.FromException(error); } }
    private static Task BeginProbeProcessDispose(HealthPipeServer server) { try { return server.DisposeAsync().AsTask(); } catch (Exception error) { return Task.FromException(error); } }
    private static WeakReference<Task> ObserveProbeProcessTerminal(Task actual, ProbeProcessStatus status, bool disposal)
    {
        Task observer = actual.ContinueWith(static (completed, state) =>
        {
            var observed = ((ProbeProcessStatus Status, bool Disposal))state!;
            if (completed.Exception is { } error) { observed.Status.RecordError(error); }
            else if (completed.IsCanceled) { observed.Status.RecordError(new TaskCanceledException("Actual uncanceled terminal operation canceled.")); }
            else { observed.Status.RecordSuccess(observed.Disposal); }
        }, (status, disposal), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new(observer);
    }
    private sealed record ProbeProcessObservation(ExceptionDispatchInfo? Primary, WeakReference[] Graph, ProbeProcessFacts Facts, ProbeProcessStatus? Status, WeakReference<Task>[] Observers);
    private sealed class ProbeProcessStatus
    {
        private readonly object gate = new();
        private readonly List<Exception> errors = [];
        private bool stop, dispose;
        internal bool StopCompleted { get { lock (gate) { return stop; } } }
        internal bool DisposeCompleted { get { lock (gate) { return dispose; } } }
        internal void RecordSuccess(bool disposal) { lock (gate) { if (disposal) { dispose = true; } else { stop = true; } } }
        internal void RecordError(Exception error) { lock (gate) { errors.Add(error); } }
        internal Exception[] Errors() { lock (gate) { return [.. errors]; } }
    }
    private sealed class ProbeProcessFacts
    {
        private readonly object gate = new();
        private readonly List<Exception> errors = [];
        internal int captures, returns, createdTimers, actualDrainsJoined, callbacksEntered, callbacksReturned;
        internal int expectedCaptures, originalAssertionsPassed, naturalExitBeforeCleanup, actualPid;
        internal int probeDisposalJoined, finishObserved, exitJoined, stdoutJoined, stderrJoined, monitorJoined, serviceJoined;
        internal void RecordChildJoin(int kind)
        {
            switch (kind)
            {
                case 0: Volatile.Write(ref finishObserved, 1); break;
                case 1: Volatile.Write(ref exitJoined, 1); break;
                case 2: Volatile.Write(ref stdoutJoined, 1); break;
                case 3: Volatile.Write(ref stderrJoined, 1); break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
        internal void RecordError(Exception error) { lock (gate) { errors.Add(error); } }
        internal Exception[] Errors() { lock (gate) { return [.. errors]; } }
    }
    private sealed class ProbeProcessSource(HealthcheckService service, ProbeProcessFacts facts) : IHealthSnapshotSource
    {
        internal int Captures => Volatile.Read(ref facts.captures);
        public HealthSnapshot CaptureSnapshot()
        {
            Interlocked.Increment(ref facts.captures);
            try { return service.CaptureSnapshot(); }
            catch (Exception error) { facts.RecordError(error); throw; }
            finally { Interlocked.Increment(ref facts.returns); }
        }
    }
    private sealed class ProbeProcessClock(ProbeProcessFacts facts) : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<ProbeProcessTimer> timers = [];
        public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;
        public override long GetTimestamp() => TimeProvider.System.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            try
            {
                var observed = new ProbeProcessTimer(facts, callback, state);
                observed.Publish(TimeProvider.System.CreateTimer(observed.Invoke, null, dueTime, period));
                lock (gate) { timers.Add(observed); }
                Interlocked.Increment(ref facts.createdTimers);
                return observed;
            }
            catch (Exception error) { facts.RecordError(error); throw; }
        }
        internal void JoinActualDisposals()
        {
            ProbeProcessTimer[] actual; lock (gate) { actual = [.. timers]; }
            var errors = new List<Exception>();
            foreach (ProbeProcessTimer timer in actual)
            {
                try { timer.JoinActualAndReleaseOnlyObserverReferences(); Interlocked.Increment(ref facts.actualDrainsJoined); }
                catch (Exception error) { errors.Add(error); }
            }
            lock (gate)
            {
                if (timers.Count != actual.Length) { throw new InvalidOperationException("Actual request timer publication raced its cleanup inventory."); }
            }
            if (errors.Count > 0) { throw new AggregateException("Actual System request timers did not all drain.", errors); }
        }
        internal WeakReference[] WeakTimerOwners()
        {
            lock (gate) { return timers.SelectMany(timer => timer.WeakOwners()).ToArray(); }
        }
        private sealed class ProbeProcessTimer(ProbeProcessFacts facts, TimerCallback callback, object? state) : ITimer
        {
            private readonly object gate = new();
            private readonly TaskCompletionSource disposalRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private TimerCallback? observedCallback = callback;
            private object? observedState = state;
            private ITimer? actual;
            private WeakReference? originalActual;
            private Task? actualDisposal;
            internal void Publish(ITimer timer) { lock (gate) { actual = timer; originalActual = new(timer); } }
            internal WeakReference[] WeakOwners() { lock (gate) { return [new(this), originalActual ?? throw new InvalidOperationException("Actual timer owner was never published.")]; } }
            internal void Invoke(object? unused)
            {
                Interlocked.Increment(ref facts.callbacksEntered);
                try
                {
                    TimerCallback current; object? currentState;
                    lock (gate) { current = observedCallback ?? throw new InvalidOperationException("Callback entered after its actual timer drain."); currentState = observedState; }
                    current(currentState);
                }
                catch (Exception error) { facts.RecordError(error); throw; }
                finally { Interlocked.Increment(ref facts.callbacksReturned); }
            }
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                ITimer timer; lock (gate) { timer = actual ?? throw new ObjectDisposedException(nameof(ProbeProcessTimer)); }
                try { return timer.Change(dueTime, period); }
                catch (Exception error) { facts.RecordError(error); throw; }
            }
            public void Dispose()
            {
                lock (gate)
                {
                    if (actualDisposal is not null) { return; }
                    try { actualDisposal = (actual ?? throw new InvalidOperationException("Actual System timer not published.")).DisposeAsync().AsTask(); }
                    catch (Exception error) { facts.RecordError(error); actualDisposal = Task.FromException(error); }
                }
                // The real disposal task can await this callback. Never self-join here.
                disposalRequested.TrySetResult();
            }
            public ValueTask DisposeAsync() { Dispose(); lock (gate) { return new(actualDisposal!); } }
            internal void JoinActualAndReleaseOnlyObserverReferences()
            {
                JoinProbeProcess(disposalRequested.Task);
                Task drain; lock (gate) { drain = actualDisposal ?? throw new InvalidOperationException("Actual System disposal task missing."); }
                JoinProbeProcess(drain);
                lock (gate) { observedCallback = null; observedState = null; actual = null; }
            }
        }
    }
}

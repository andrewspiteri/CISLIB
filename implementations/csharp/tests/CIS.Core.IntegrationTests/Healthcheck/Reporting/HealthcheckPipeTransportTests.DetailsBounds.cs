using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Healthcheck.Reporting;

public sealed partial class HealthcheckPipeTransportTests
{
    private static readonly TimeSpan DetailsBoundsDeadline = TransportTestSupport.Limit;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static DetailsBoundsObservation RunDetailsBoundsScenario(bool detailsEnabled)
    {
        var (service, monitor) = Create();
        var extra = new List<IConditionMonitor>();
        string pipe = TransportTestSupport.NewPipeName();
        var facts = new DetailsBoundsFacts();
        var source = new DetailsBoundsSource(service, facts);
        var clock = new DetailsBoundsClock(facts);
        var callers = new DetailsBoundsCallers();
        HealthPipeServer? server = null;
        Task? body = null;
        Exception? primary = null;
        try
        {
            for (int i = 0; i < 30; i++) { extra.Add(service.CreateCondition(new ConditionMonitorOptions { IsCritical = false, InitiallyFlagged = false, Name = new string('x', 128) })); }
            server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe, DetailsEnabled = detailsEnabled, MaxReportBytes = 1024 }, clock);
            body = RunDetailsBoundsBodyAsync(server, pipe, source, monitor, extra, callers, facts, detailsEnabled);
            JoinDetailsBounds(body);
        }
        catch (Exception error) { primary = error; }
        return ReleaseDetailsBoundsOwners(server, service, monitor, source, clock, facts, body, callers, extra, primary, detailsEnabled ? 2 : 1);
    }

    private static async Task RunDetailsBoundsBodyAsync(HealthPipeServer server, string pipe, DetailsBoundsSource source,
        IConditionMonitor monitor, List<IConditionMonitor> extra, DetailsBoundsCallers callers, DetailsBoundsFacts facts, bool detailsEnabled)
    {
        await TransportTestSupport.StartServerAsync(server, pipe);
        Task<JsonObject> details = TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"details\"}");
        callers.Requests.Add(details);
        TransportTestSupport.ValidateError(await details, detailsEnabled ? "ReportTooLarge" : "DetailsDisabled");
        Volatile.Write(ref facts.beforeRecoveryCaptures, source.Captures);
        Task<JsonObject> recovery = TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}");
        callers.Requests.Add(recovery);
        TransportTestSupport.ValidateSnapshotEnvelope(await recovery, "health", true, true, 31);
        GC.KeepAlive(extra); GC.KeepAlive(monitor);
    }

    private sealed class DetailsBoundsCallers
    {
        internal List<Task<JsonObject>> Requests { get; } = [];
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static DetailsBoundsObservation ReleaseDetailsBoundsOwners(HealthPipeServer? server,
        HealthcheckService service, IConditionMonitor monitor, DetailsBoundsSource source, DetailsBoundsClock clock,
        DetailsBoundsFacts facts, Task? body, DetailsBoundsCallers callers, List<IConditionMonitor> extra, Exception? primary, int expectedCaptures)
    {
        Task? stopping = server is null ? null : BeginDetailsBoundsStop(server);
        Task? disposing = server is null ? null : BeginDetailsBoundsDispose(server);
        var status = new DetailsBoundsStatus();
        var observers = new List<WeakReference<Task>>();
        if (stopping is not null) { observers.Add(ObserveDetailsBoundsTerminal(stopping, status, false)); }
        if (disposing is not null) { observers.Add(ObserveDetailsBoundsTerminal(disposing, status, true)); }
        var holds = new List<Exception>();
        try
        {
            // These are the original real helper tasks, including their frame IO,
            // await-using client disposal and caller CTS disposal. No IO catch is added.
            foreach (Task? actual in new Task?[] { body }.Concat(callers.Requests))
            {
                if (actual is not null)
                {
                    try
                    {
                        JoinDetailsBounds(actual);
                        if (ReferenceEquals(actual, body)) { Volatile.Write(ref facts.bodyJoined, 1); }
                        else { Interlocked.Increment(ref facts.clientTasksJoined); }
                    }
                    catch (Exception error) when (ReferenceEquals(actual, body) && ReferenceEquals(error, primary))
                    {
                        // The same actual body task already completed, including its
                        // original await-using disposals, with this exact saved error.
                        // Preserve it as the body failure, not a second cleanup fault.
                        Volatile.Write(ref facts.bodyJoined, 1);
                    }
                    catch (Exception error) { holds.Add(error); }
                }
            }
            try { clock.JoinActualDisposals(); }
            catch (Exception error) { holds.Add(error); }
        }
        catch (Exception error) { holds.Add(error); }
        finally
        {
            foreach (IConditionMonitor owned in extra)
            {
                try { JoinDetailsBounds(owned.DisposeAsync().AsTask()); Interlocked.Increment(ref facts.extraMonitorsJoined); }
                catch (Exception error) { holds.Add(error); }
            }
            try { JoinDetailsBounds(monitor.DisposeAsync().AsTask()); Volatile.Write(ref facts.monitorJoined, 1); }
            catch (Exception error) { holds.Add(error); }
            try { JoinDetailsBounds(service.DisposeAsync().AsTask()); Volatile.Write(ref facts.serviceJoined, 1); }
            catch (Exception error) { holds.Add(error); }
            GC.KeepAlive(monitor);
        }
        // Keep actual swallowed source/callback causes visible even when the
        // original response assertion also failed. They remain resource holds.
        holds.AddRange(facts.Errors());
        bool eligible = primary is null && holds.Count == 0 && server is not null && body is not null && callers.Requests.Count == 2
            && !stopping!.IsFaulted && !stopping.IsCanceled && !disposing!.IsFaulted && !disposing.IsCanceled;
        if (eligible)
        {
            try { RequireDetailsBoundsPhase(facts, expectedCaptures); }
            catch (Exception error) { holds.Add(error); eligible = false; }
        }
        if (eligible)
        {
            // Exact remaining-decrement leaves only: the real BeginStop still
            // cancels admission and disposes every published stream. Every actual
            // accepting/handling RunSlot still owns Server until IO and its finally
            // return. All eight original workers belong to this whole weak
            // graph; neither method changes the default slot population.
            WeakReference[] graph = [new(server!), new(service), new(monitor), new(source), new(clock), new(callers), new(extra), .. extra.Select(owned => new WeakReference(owned)), .. clock.WeakTimerOwners()];
            return new(null, graph, facts, status, [.. observers], expectedCaptures);
        }
        if (server is not null)
        {
            Task all = Task.WhenAll(stopping!, disposing!);
            try { JoinDetailsBounds(all); }
            catch (Exception error) { holds.Add(all.Exception ?? error); }
            foreach (WeakReference<Task> observer in observers)
            {
                try { if (observer.TryGetTarget(out Task? actual)) { JoinDetailsBounds(actual); } }
                catch (Exception error) { holds.Add(error); }
            }
            holds.AddRange(status.Errors());
        }
        if (holds.Count > 0)
        {
            if (primary is not null) { holds.Insert(0, primary); }
            primary = new AggregateException("The original details-bounds body or its actual resource cleanup did not settle.", holds);
        }
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), [], facts, null, [], expectedCaptures);
    }

    private static void ReviewDetailsBoundsScenario(DetailsBoundsObservation observed)
    {
        if (observed.Graph.Length > 0)
        {
            var elapsed = Stopwatch.StartNew();
            while (observed.Graph.Any(reference => reference.IsAlive) && elapsed.Elapsed < DetailsBoundsDeadline)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                if (observed.Graph.Any(reference => reference.IsAlive) && elapsed.Elapsed < DetailsBoundsDeadline) { Thread.Sleep(1); }
            }
            if (observed.Graph.Any(reference => reference.IsAlive))
            {
                throw new InvalidOperationException("Actual accepting/request/source/clock/timer ownership remains live; elapsed time cannot certify settlement.");
            }
            RequireDetailsBoundsPhase(observed.Facts, observed.ExpectedCaptures);
            foreach (WeakReference<Task> reference in observed.Observers)
            {
                if (reference.TryGetTarget(out Task? actual)) { JoinDetailsBounds(actual); }
            }
            Exception[] errors = observed.Status!.Errors();
            if (errors.Length > 0) { throw new AggregateException("An actual uncanceled terminal operation failed.", errors); }
        }
        observed.Primary?.Throw();
        if (observed.Graph.Length > 0)
        {
            Assert.IsTrue(observed.Status!.StopCompleted, "Actual terminal Stop must publish success after the real details-size exchange, timers and all queued workers settle.");
            Assert.IsTrue(observed.Status.DisposeCompleted, "Actual terminal Dispose must publish success after the real details-size exchange, timers and all queued workers settle.");
        }
    }

    private static void RequireDetailsBoundsPhase(DetailsBoundsFacts facts, int expectedCaptures)
    {
        Exception[] errors = facts.Errors();
        if (errors.Length > 0) { throw new AggregateException("An actual source, clock or timer callback failure remains a resource hold.", errors); }
        if (expectedCaptures is not (1 or 2)) { throw new InvalidOperationException("Only the original disabled/oversized details source inventories are supported."); }
        if (Volatile.Read(ref facts.captures) != expectedCaptures || Volatile.Read(ref facts.returns) != expectedCaptures
            || Volatile.Read(ref facts.beforeRecoveryCaptures) != expectedCaptures - 1
            || Volatile.Read(ref facts.createdTimers) != 2 || Volatile.Read(ref facts.actualDrainsJoined) != 2
            || Volatile.Read(ref facts.callbacksEntered) != Volatile.Read(ref facts.callbacksReturned)
            || Volatile.Read(ref facts.clientTasksJoined) != 2 || Volatile.Read(ref facts.bodyJoined) != 1
            || Volatile.Read(ref facts.extraMonitorsJoined) != 30 || Volatile.Read(ref facts.monitorJoined) != 1 || Volatile.Read(ref facts.serviceJoined) != 1)
        {
            throw new InvalidOperationException("The original two actual requests and System budget drains, disabled0-to1 or oversized1-to2 returned source captures, all31 monitor disposals and actual service disposal must join; unknown phases remain holds.");
        }
    }
    private static void JoinDetailsBounds(Task actual) => actual.WaitAsync(DetailsBoundsDeadline).GetAwaiter().GetResult();
    private static T JoinDetailsBounds<T>(Task<T> actual) => actual.WaitAsync(DetailsBoundsDeadline).GetAwaiter().GetResult();
    private static Task BeginDetailsBoundsStop(HealthPipeServer server)
    {
        try { return server.StopAsync(); }
        catch (Exception error) { return Task.FromException(error); }
    }
    private static Task BeginDetailsBoundsDispose(HealthPipeServer server)
    {
        try { return server.DisposeAsync().AsTask(); }
        catch (Exception error) { return Task.FromException(error); }
    }
    private static WeakReference<Task> ObserveDetailsBoundsTerminal(Task actual, DetailsBoundsStatus status, bool disposal)
    {
        Task observer = actual.ContinueWith(static (completed, state) =>
        {
            var observed = ((DetailsBoundsStatus Status, bool Disposal))state!;
            if (completed.Exception is { } error) { observed.Status.RecordError(error); }
            else if (completed.IsCanceled) { observed.Status.RecordError(new TaskCanceledException("Actual uncanceled terminal operation canceled.")); }
            else { observed.Status.RecordSuccess(observed.Disposal); }
        }, (status, disposal), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new(observer);
    }
    private sealed record DetailsBoundsObservation(ExceptionDispatchInfo? Primary, WeakReference[] Graph,
        DetailsBoundsFacts Facts, DetailsBoundsStatus? Status, WeakReference<Task>[] Observers, int ExpectedCaptures);
    private sealed class DetailsBoundsStatus
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
    private sealed class DetailsBoundsFacts
    {
        private readonly object gate = new();
        private readonly List<Exception> errors = [];
        internal int captures, returns, createdTimers, actualDrainsJoined, callbacksEntered, callbacksReturned;
        internal int clientTasksJoined, bodyJoined, beforeRecoveryCaptures = -1, extraMonitorsJoined, monitorJoined, serviceJoined;
        internal void RecordError(Exception error) { lock (gate) { errors.Add(error); } }
        internal Exception[] Errors() { lock (gate) { return [.. errors]; } }
    }
    private sealed class DetailsBoundsSource(HealthcheckService service, DetailsBoundsFacts facts) : IHealthSnapshotSource
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
    private sealed class DetailsBoundsClock(DetailsBoundsFacts facts) : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<DetailsBoundsTimer> timers = [];
        public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;
        public override long GetTimestamp() => TimeProvider.System.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            try
            {
                var observed = new DetailsBoundsTimer(facts, callback, state);
                observed.Publish(TimeProvider.System.CreateTimer(observed.Invoke, null, dueTime, period));
                lock (gate) { timers.Add(observed); }
                Interlocked.Increment(ref facts.createdTimers);
                return observed;
            }
            catch (Exception error) { facts.RecordError(error); throw; }
        }
        internal void JoinActualDisposals()
        {
            DetailsBoundsTimer[] actual; lock (gate) { actual = [.. timers]; }
            var errors = new List<Exception>();
            foreach (DetailsBoundsTimer timer in actual)
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
        private sealed class DetailsBoundsTimer(DetailsBoundsFacts facts, TimerCallback callback, object? state) : ITimer
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
                ITimer timer; lock (gate) { timer = actual ?? throw new ObjectDisposedException(nameof(DetailsBoundsTimer)); }
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
                JoinDetailsBounds(disposalRequested.Task);
                Task drain; lock (gate) { drain = actualDisposal ?? throw new InvalidOperationException("Actual System disposal task missing."); }
                JoinDetailsBounds(drain);
                lock (gate) { observedCallback = null; observedState = null; actual = null; }
            }
        }
    }
}

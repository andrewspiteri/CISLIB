using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Healthcheck.Reporting;

public sealed partial class HealthcheckPipeTransportTests
{
    private static readonly TimeSpan InvalidRequestDeadline = TransportTestSupport.Limit;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static InvalidRequestObservation RunInvalidRequestScenario(string request, string code, HealthcheckService? controlService = null, IConditionMonitor? controlMonitor = null)
    {
        // Public fixture paths use the original owner construction. Only private
        // real-body fault controls supply separately constructed real owners.
        if ((controlService is null) != (controlMonitor is null)) { throw new ArgumentException("Both actual control owners must be supplied together."); }
        var (service, monitor) = controlService is null ? Create() : (controlService, controlMonitor!);
        string pipe = TransportTestSupport.NewPipeName();
        var facts = new InvalidRequestFacts();
        var source = new InvalidRequestSource(service, facts);
        var clock = new InvalidRequestClock(facts);
        HealthPipeServer? server = null;
        Task<JsonObject>? invalid = null, recovery = null;
        Exception? primary = null;
        try
        {
            // Preserve the original default eight slots and every timeout/size option.
            server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe }, clock);
            JoinInvalidRequest(TransportTestSupport.StartServerAsync(server, pipe));
            invalid = TransportTestSupport.RequestAsync(pipe, request, fragmented: true);
            JsonObject response = JoinInvalidRequest(invalid);
            TransportTestSupport.ValidateError(response, code);
            Assert.AreEqual(1, (int?)response["protocolVersion"]);
            Assert.AreEqual(1, (int?)response["schemaVersion"]);
            Assert.IsNull(response["view"]);
            Assert.AreEqual(0, source.Captures);
            recovery = TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}");
            TransportTestSupport.ValidateSnapshotEnvelope(JoinInvalidRequest(recovery), "health", true, true);
            GC.KeepAlive(monitor);
        }
        catch (Exception error) { primary = error; }
        return ReleaseInvalidRequestOwners(server, service, monitor, source, clock, facts, invalid, recovery, primary);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static InvalidRequestObservation ReleaseInvalidRequestOwners(HealthPipeServer? server,
        HealthcheckService service, IConditionMonitor monitor, InvalidRequestSource source, InvalidRequestClock clock,
        InvalidRequestFacts facts, Task<JsonObject>? invalid, Task<JsonObject>? recovery, Exception? primary)
    {
        Task? stopping = server is null ? null : BeginInvalidRequestStop(server);
        Task? disposing = server is null ? null : BeginInvalidRequestDispose(server);
        var status = new InvalidRequestStatus();
        var observers = new List<WeakReference<Task>>();
        if (stopping is not null) { observers.Add(ObserveInvalidRequestTerminal(stopping, status, false)); }
        if (disposing is not null) { observers.Add(ObserveInvalidRequestTerminal(disposing, status, true)); }
        var holds = new List<Exception>();
        try
        {
            // These are the original real helper tasks, including their frame IO,
            // await-using client disposal and caller CTS disposal. No IO catch is added.
            foreach (Task<JsonObject>? actual in new[] { invalid, recovery })
            {
                if (actual is not null)
                {
                    try { _ = JoinInvalidRequest(actual); Interlocked.Increment(ref facts.clientTasksJoined); }
                    catch (Exception error) { holds.Add(error); }
                }
            }
            try { clock.JoinActualDisposals(); }
            catch (Exception error) { holds.Add(error); }
        }
        catch (Exception error) { holds.Add(error); }
        finally
        {
            try { JoinInvalidRequest(monitor.DisposeAsync().AsTask()); Volatile.Write(ref facts.monitorJoined, 1); }
            catch (Exception error) { holds.Add(error); }
            try { JoinInvalidRequest(service.DisposeAsync().AsTask()); Volatile.Write(ref facts.serviceJoined, 1); }
            catch (Exception error) { holds.Add(error); }
            GC.KeepAlive(monitor);
        }
        // Preserve an actual producer/callback cause even when its error
        // envelope also failed an original body assertion.
        holds.AddRange(facts.Errors());
        bool eligible = primary is null && holds.Count == 0 && server is not null && invalid is not null && recovery is not null
            && !stopping!.IsFaulted && !stopping.IsCanceled && !disposing!.IsFaulted && !disposing.IsCanceled;
        if (eligible)
        {
            try { RequireInvalidRequestPhase(facts); }
            catch (Exception error) { holds.Add(error); eligible = false; }
        }
        if (eligible)
        {
            // Exact remaining-decrement leaves only: the real BeginStop still
            // cancels admission and disposes every published stream. Every actual
            // accepting/handling RunSlot still owns Server until IO and its finally
            // return. All eight real workers belong to this whole weak graph.
            WeakReference[] graph = [new(server!), new(service), new(monitor), new(source), new(clock), .. clock.WeakTimerOwners()];
            return new(null, graph, facts, status, [.. observers]);
        }
        if (server is not null)
        {
            Task all = Task.WhenAll(stopping!, disposing!);
            try { JoinInvalidRequest(all); }
            catch (Exception error) { holds.Add(all.Exception ?? error); }
            foreach (WeakReference<Task> observer in observers)
            {
                try { if (observer.TryGetTarget(out Task? actual)) { JoinInvalidRequest(actual); } }
                catch (Exception error) { holds.Add(error); }
            }
            holds.AddRange(status.Errors());
        }
        if (holds.Count > 0)
        {
            if (primary is not null) { holds.Insert(0, primary); }
            primary = new AggregateException("The original malformed-request body or its actual resource cleanup did not settle.", holds);
        }
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), [], facts, null, []);
    }

    private static void ReviewInvalidRequestScenario(InvalidRequestObservation observed)
    {
        if (observed.Graph.Length > 0)
        {
            var elapsed = Stopwatch.StartNew();
            while (observed.Graph.Any(reference => reference.IsAlive) && elapsed.Elapsed < InvalidRequestDeadline)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                if (observed.Graph.Any(reference => reference.IsAlive) && elapsed.Elapsed < InvalidRequestDeadline) { Thread.Sleep(1); }
            }
            if (observed.Graph.Any(reference => reference.IsAlive))
            {
                throw new InvalidOperationException("Actual accepting/request/source/clock/timer ownership remains live; elapsed time cannot certify settlement.");
            }
            RequireInvalidRequestPhase(observed.Facts);
            foreach (WeakReference<Task> reference in observed.Observers)
            {
                if (reference.TryGetTarget(out Task? actual)) { JoinInvalidRequest(actual); }
            }
            Exception[] errors = observed.Status!.Errors();
            if (errors.Length > 0) { throw new AggregateException("An actual uncanceled terminal operation failed.", errors); }
        }
        observed.Primary?.Throw();
        if (observed.Graph.Length > 0)
        {
            Assert.IsTrue(observed.Status!.StopCompleted, "Actual terminal Stop must publish success after the real malformed request, recovery exchange, timers and all queued workers settle.");
            Assert.IsTrue(observed.Status.DisposeCompleted, "Actual terminal Dispose must publish success after the real malformed request, recovery exchange, timers and all queued workers settle.");
        }
    }

    private static void RequireInvalidRequestPhase(InvalidRequestFacts facts)
    {
        Exception[] errors = facts.Errors();
        if (errors.Length > 0) { throw new AggregateException("An actual source, clock or timer callback failure remains a resource hold.", errors); }
        if (Volatile.Read(ref facts.captures) != 1 || Volatile.Read(ref facts.returns) != 1
            || Volatile.Read(ref facts.createdTimers) != 2 || Volatile.Read(ref facts.actualDrainsJoined) != 2
            || Volatile.Read(ref facts.callbacksEntered) != Volatile.Read(ref facts.callbacksReturned)
            || Volatile.Read(ref facts.clientTasksJoined) != 2 || Volatile.Read(ref facts.monitorJoined) != 1 || Volatile.Read(ref facts.serviceJoined) != 1)
        {
            throw new InvalidOperationException("Exactly two actual client tasks and System timer drains, one returned source capture, and real monitor/service joins are required; unknown phases remain holds.");
        }
    }
    private static void JoinInvalidRequest(Task actual) => actual.WaitAsync(InvalidRequestDeadline).GetAwaiter().GetResult();
    private static T JoinInvalidRequest<T>(Task<T> actual) => actual.WaitAsync(InvalidRequestDeadline).GetAwaiter().GetResult();
    private static Task BeginInvalidRequestStop(HealthPipeServer server)
    {
        try { return server.StopAsync(); }
        catch (Exception error) { return Task.FromException(error); }
    }
    private static Task BeginInvalidRequestDispose(HealthPipeServer server)
    {
        try { return server.DisposeAsync().AsTask(); }
        catch (Exception error) { return Task.FromException(error); }
    }
    private static WeakReference<Task> ObserveInvalidRequestTerminal(Task actual, InvalidRequestStatus status, bool disposal)
    {
        Task observer = actual.ContinueWith(static (completed, state) =>
        {
            var observed = ((InvalidRequestStatus Status, bool Disposal))state!;
            if (completed.Exception is { } error) { observed.Status.RecordError(error); }
            else if (completed.IsCanceled) { observed.Status.RecordError(new TaskCanceledException("Actual uncanceled terminal operation canceled.")); }
            else { observed.Status.RecordSuccess(observed.Disposal); }
        }, (status, disposal), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new(observer);
    }
    private sealed record InvalidRequestObservation(ExceptionDispatchInfo? Primary, WeakReference[] Graph,
        InvalidRequestFacts Facts, InvalidRequestStatus? Status, WeakReference<Task>[] Observers);
    private sealed class InvalidRequestStatus
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
    private sealed class InvalidRequestFacts
    {
        private readonly object gate = new();
        private readonly List<Exception> errors = [];
        internal int captures, returns, createdTimers, actualDrainsJoined, callbacksEntered, callbacksReturned;
        internal int clientTasksJoined, monitorJoined, serviceJoined;
        internal void RecordError(Exception error) { lock (gate) { errors.Add(error); } }
        internal Exception[] Errors() { lock (gate) { return [.. errors]; } }
    }
    private sealed class InvalidRequestSource(HealthcheckService service, InvalidRequestFacts facts) : IHealthSnapshotSource
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
    private sealed class InvalidRequestClock(InvalidRequestFacts facts) : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<InvalidRequestTimer> timers = [];
        public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;
        public override long GetTimestamp() => TimeProvider.System.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            try
            {
                var observed = new InvalidRequestTimer(facts, callback, state);
                observed.Publish(TimeProvider.System.CreateTimer(observed.Invoke, null, dueTime, period));
                lock (gate) { timers.Add(observed); }
                Interlocked.Increment(ref facts.createdTimers);
                return observed;
            }
            catch (Exception error) { facts.RecordError(error); throw; }
        }
        internal void JoinActualDisposals()
        {
            InvalidRequestTimer[] actual; lock (gate) { actual = [.. timers]; }
            var errors = new List<Exception>();
            foreach (InvalidRequestTimer timer in actual)
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
        private sealed class InvalidRequestTimer(InvalidRequestFacts facts, TimerCallback callback, object? state) : ITimer
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
                ITimer timer; lock (gate) { timer = actual ?? throw new ObjectDisposedException(nameof(InvalidRequestTimer)); }
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
                JoinInvalidRequest(disposalRequested.Task);
                Task drain; lock (gate) { drain = actualDisposal ?? throw new InvalidOperationException("Actual System disposal task missing."); }
                JoinInvalidRequest(drain);
                lock (gate) { observedCallback = null; observedState = null; actual = null; }
            }
        }
    }
}

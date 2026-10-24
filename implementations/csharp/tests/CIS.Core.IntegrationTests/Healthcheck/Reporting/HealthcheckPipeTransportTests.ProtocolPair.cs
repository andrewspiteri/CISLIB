using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Healthcheck.Reporting;

public sealed partial class HealthcheckPipeTransportTests
{
    private static readonly TimeSpan ProtocolPairDeadline = TransportTestSupport.Limit;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ProtocolPairObservation RunUnknownFieldsScenario()
    {
        var (service, monitor) = Create();
        string pipe = TransportTestSupport.NewPipeName();
        var facts = new ProtocolPairFacts();
        var source = new ProtocolPairSource(service, facts);
        var clock = new ProtocolPairClock(facts);
        var callers = new ProtocolPairCallers();
        HealthPipeServer? server = null;
        Task? body = null;
        Exception? primary = null;
        try
        {
            server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe }, clock);
            body = RunUnknownFieldsBodyAsync(server, pipe, source, monitor, callers, facts);
            JoinProtocolPair(body);
        }
        catch (Exception error) { primary = error; }
        return ReleaseProtocolPairOwners(server, service, monitor, source, clock, facts, body, callers, primary, 2);
    }

    private static async Task RunUnknownFieldsBodyAsync(HealthPipeServer server, string pipe, ProtocolPairSource source,
        IConditionMonitor monitor, ProtocolPairCallers callers, ProtocolPairFacts facts)
    {
        await TransportTestSupport.StartServerAsync(server, pipe);
        string nested = "0";
        for (int i = 0; i < 16; i++) { nested = "{\"x\":" + nested + "}"; }
        Task<JsonObject> invalid = TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\",\"unknown\":" + nested + "}");
        callers.Requests.Add(invalid);
        TransportTestSupport.ValidateError(await invalid, "InvalidRequest");
        Volatile.Write(ref facts.beforeRecoveryCaptures, source.Captures);
        Task<JsonObject> recovery = TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\",\"future\":{\"v\":[true,12]}}");
        callers.Requests.Add(recovery);
        TransportTestSupport.ValidateSnapshotEnvelope(await recovery, "health", true, true);
        GC.KeepAlive(monitor);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ProtocolPairObservation RunMaximumRequestScenario()
    {
        var (service, monitor) = Create();
        string pipe = TransportTestSupport.NewPipeName();
        var facts = new ProtocolPairFacts();
        var source = new ProtocolPairSource(service, facts);
        var clock = new ProtocolPairClock(facts);
        var callers = new ProtocolPairCallers();
        HealthPipeServer? server = null;
        Task? body = null;
        Exception? primary = null;
        try
        {
            server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe }, clock);
            body = RunMaximumRequestBodyAsync(server, pipe, monitor, callers);
            JoinProtocolPair(body);
        }
        catch (Exception error) { primary = error; }
        return ReleaseProtocolPairOwners(server, service, monitor, source, clock, facts, body, callers, primary, 1);
    }

    private static async Task RunMaximumRequestBodyAsync(HealthPipeServer server, string pipe, IConditionMonitor monitor, ProtocolPairCallers callers)
    {
        await TransportTestSupport.StartServerAsync(server, pipe);
        const string request = "{\"protocolVersion\":1,\"view\":\"health\"}";
        Task<JsonObject> complete = TransportTestSupport.RequestAsync(pipe, request.PadRight(512));
        callers.Requests.Add(complete);
        TransportTestSupport.ValidateSnapshotEnvelope(await complete, "health", true, true);
        GC.KeepAlive(monitor);
    }

    private sealed class ProtocolPairCallers
    {
        internal List<Task<JsonObject>> Requests { get; } = [];
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ProtocolPairObservation ReleaseProtocolPairOwners(HealthPipeServer? server,
        HealthcheckService service, IConditionMonitor monitor, ProtocolPairSource source, ProtocolPairClock clock,
        ProtocolPairFacts facts, Task? body, ProtocolPairCallers callers, Exception? primary, int expectedRequests)
    {
        Task? stopping = server is null ? null : BeginProtocolPairStop(server);
        Task? disposing = server is null ? null : BeginProtocolPairDispose(server);
        var status = new ProtocolPairStatus();
        var observers = new List<WeakReference<Task>>();
        if (stopping is not null) { observers.Add(ObserveProtocolPairTerminal(stopping, status, false)); }
        if (disposing is not null) { observers.Add(ObserveProtocolPairTerminal(disposing, status, true)); }
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
                        JoinProtocolPair(actual);
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
            try { JoinProtocolPair(monitor.DisposeAsync().AsTask()); Volatile.Write(ref facts.monitorJoined, 1); }
            catch (Exception error) { holds.Add(error); }
            try { JoinProtocolPair(service.DisposeAsync().AsTask()); Volatile.Write(ref facts.serviceJoined, 1); }
            catch (Exception error) { holds.Add(error); }
            GC.KeepAlive(monitor);
        }
        // Keep actual swallowed source/callback causes visible even when the
        // original response assertion also failed. They remain resource holds.
        holds.AddRange(facts.Errors());
        bool eligible = primary is null && holds.Count == 0 && server is not null && body is not null && callers.Requests.Count == expectedRequests
            && !stopping!.IsFaulted && !stopping.IsCanceled && !disposing!.IsFaulted && !disposing.IsCanceled;
        if (eligible)
        {
            try { RequireProtocolPairPhase(facts, expectedRequests); }
            catch (Exception error) { holds.Add(error); eligible = false; }
        }
        if (eligible)
        {
            // Exact remaining-decrement leaves only: the real BeginStop still
            // cancels admission and disposes every published stream. Every actual
            // accepting/handling RunSlot still owns Server until IO and its finally
            // return. All eight original workers belong to this whole weak
            // graph; neither method changes the default slot population.
            WeakReference[] graph = [new(server!), new(service), new(monitor), new(source), new(clock), new(callers), .. clock.WeakTimerOwners()];
            return new(null, graph, facts, status, [.. observers], expectedRequests);
        }
        if (server is not null)
        {
            Task all = Task.WhenAll(stopping!, disposing!);
            try { JoinProtocolPair(all); }
            catch (Exception error) { holds.Add(all.Exception ?? error); }
            foreach (WeakReference<Task> observer in observers)
            {
                try { if (observer.TryGetTarget(out Task? actual)) { JoinProtocolPair(actual); } }
                catch (Exception error) { holds.Add(error); }
            }
            holds.AddRange(status.Errors());
        }
        if (holds.Count > 0)
        {
            if (primary is not null) { holds.Insert(0, primary); }
            primary = new AggregateException("The original protocol-pair body or its actual resource cleanup did not settle.", holds);
        }
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), [], facts, null, [], expectedRequests);
    }

    private static void ReviewProtocolPairScenario(ProtocolPairObservation observed)
    {
        if (observed.Graph.Length > 0)
        {
            var elapsed = Stopwatch.StartNew();
            while (observed.Graph.Any(reference => reference.IsAlive) && elapsed.Elapsed < ProtocolPairDeadline)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                if (observed.Graph.Any(reference => reference.IsAlive) && elapsed.Elapsed < ProtocolPairDeadline) { Thread.Sleep(1); }
            }
            if (observed.Graph.Any(reference => reference.IsAlive))
            {
                throw new InvalidOperationException("Actual accepting/request/source/clock/timer ownership remains live; elapsed time cannot certify settlement.");
            }
            RequireProtocolPairPhase(observed.Facts, observed.ExpectedRequests);
            foreach (WeakReference<Task> reference in observed.Observers)
            {
                if (reference.TryGetTarget(out Task? actual)) { JoinProtocolPair(actual); }
            }
            Exception[] errors = observed.Status!.Errors();
            if (errors.Length > 0) { throw new AggregateException("An actual uncanceled terminal operation failed.", errors); }
        }
        observed.Primary?.Throw();
        if (observed.Graph.Length > 0)
        {
            Assert.IsTrue(observed.Status!.StopCompleted, "Actual terminal Stop must publish success after the real protocol exchange, timers and all queued workers settle.");
            Assert.IsTrue(observed.Status.DisposeCompleted, "Actual terminal Dispose must publish success after the real protocol exchange, timers and all queued workers settle.");
        }
    }

    private static void RequireProtocolPairPhase(ProtocolPairFacts facts, int expectedRequests)
    {
        Exception[] errors = facts.Errors();
        if (errors.Length > 0) { throw new AggregateException("An actual source, clock or timer callback failure remains a resource hold.", errors); }
        if (expectedRequests is not (1 or 2)) { throw new InvalidOperationException("Only the two original protocol methods' actual request inventories are supported."); }
        if (Volatile.Read(ref facts.captures) != 1 || Volatile.Read(ref facts.returns) != 1
            || Volatile.Read(ref facts.createdTimers) != expectedRequests || Volatile.Read(ref facts.actualDrainsJoined) != expectedRequests
            || Volatile.Read(ref facts.callbacksEntered) != Volatile.Read(ref facts.callbacksReturned)
            || Volatile.Read(ref facts.clientTasksJoined) != expectedRequests || Volatile.Read(ref facts.bodyJoined) != 1
            || (expectedRequests == 2 && Volatile.Read(ref facts.beforeRecoveryCaptures) != 0)
            || Volatile.Read(ref facts.monitorJoined) != 1 || Volatile.Read(ref facts.serviceJoined) != 1)
        {
            throw new InvalidOperationException("The actual original body, complete request/client scopes and every System timer drain, one returned source capture, zero source before valid additive recovery, and real state disposal must all join; unknown phases remain holds.");
        }
    }
    private static void JoinProtocolPair(Task actual) => actual.WaitAsync(ProtocolPairDeadline).GetAwaiter().GetResult();
    private static T JoinProtocolPair<T>(Task<T> actual) => actual.WaitAsync(ProtocolPairDeadline).GetAwaiter().GetResult();
    private static Task BeginProtocolPairStop(HealthPipeServer server)
    {
        try { return server.StopAsync(); }
        catch (Exception error) { return Task.FromException(error); }
    }
    private static Task BeginProtocolPairDispose(HealthPipeServer server)
    {
        try { return server.DisposeAsync().AsTask(); }
        catch (Exception error) { return Task.FromException(error); }
    }
    private static WeakReference<Task> ObserveProtocolPairTerminal(Task actual, ProtocolPairStatus status, bool disposal)
    {
        Task observer = actual.ContinueWith(static (completed, state) =>
        {
            var observed = ((ProtocolPairStatus Status, bool Disposal))state!;
            if (completed.Exception is { } error) { observed.Status.RecordError(error); }
            else if (completed.IsCanceled) { observed.Status.RecordError(new TaskCanceledException("Actual uncanceled terminal operation canceled.")); }
            else { observed.Status.RecordSuccess(observed.Disposal); }
        }, (status, disposal), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new(observer);
    }
    private sealed record ProtocolPairObservation(ExceptionDispatchInfo? Primary, WeakReference[] Graph,
        ProtocolPairFacts Facts, ProtocolPairStatus? Status, WeakReference<Task>[] Observers, int ExpectedRequests);
    private sealed class ProtocolPairStatus
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
    private sealed class ProtocolPairFacts
    {
        private readonly object gate = new();
        private readonly List<Exception> errors = [];
        internal int captures, returns, createdTimers, actualDrainsJoined, callbacksEntered, callbacksReturned;
        internal int clientTasksJoined, bodyJoined, beforeRecoveryCaptures = -1, monitorJoined, serviceJoined;
        internal void RecordError(Exception error) { lock (gate) { errors.Add(error); } }
        internal Exception[] Errors() { lock (gate) { return [.. errors]; } }
    }
    private sealed class ProtocolPairSource(HealthcheckService service, ProtocolPairFacts facts) : IHealthSnapshotSource
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
    private sealed class ProtocolPairClock(ProtocolPairFacts facts) : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<ProtocolPairTimer> timers = [];
        public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;
        public override long GetTimestamp() => TimeProvider.System.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            try
            {
                var observed = new ProtocolPairTimer(facts, callback, state);
                observed.Publish(TimeProvider.System.CreateTimer(observed.Invoke, null, dueTime, period));
                lock (gate) { timers.Add(observed); }
                Interlocked.Increment(ref facts.createdTimers);
                return observed;
            }
            catch (Exception error) { facts.RecordError(error); throw; }
        }
        internal void JoinActualDisposals()
        {
            ProtocolPairTimer[] actual; lock (gate) { actual = [.. timers]; }
            var errors = new List<Exception>();
            foreach (ProtocolPairTimer timer in actual)
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
        private sealed class ProtocolPairTimer(ProtocolPairFacts facts, TimerCallback callback, object? state) : ITimer
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
                ITimer timer; lock (gate) { timer = actual ?? throw new ObjectDisposedException(nameof(ProtocolPairTimer)); }
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
                JoinProtocolPair(disposalRequested.Task);
                Task drain; lock (gate) { drain = actualDisposal ?? throw new InvalidOperationException("Actual System disposal task missing."); }
                JoinProtocolPair(drain);
                lock (gate) { observedCallback = null; observedState = null; actual = null; }
            }
        }
    }
}

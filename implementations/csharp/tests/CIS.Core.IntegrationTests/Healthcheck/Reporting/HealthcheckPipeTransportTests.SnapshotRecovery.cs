using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Healthcheck.Reporting;

public sealed partial class HealthcheckPipeTransportTests
{
    private static readonly TimeSpan SnapshotRecoveryDeadline = TransportTestSupport.Limit;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static SnapshotRecoveryObservation RunSnapshotRecoveryScenario()
    {
        var (service, monitor) = Create();
        string pipe = TransportTestSupport.NewPipeName();
        var facts = new SnapshotRecoveryFacts();
        var source = new SnapshotRecoverySource(service, facts) { Throw = true };
        var clock = new SnapshotRecoveryClock(facts);
        var callers = new SnapshotRecoveryCallers();
        HealthPipeServer? server = null;
        Task? body = null;
        Exception? primary = null;
        try
        {
            server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe }, clock);
            body = RunSnapshotRecoveryBodyAsync(server, pipe, source, monitor, callers, facts);
            JoinSnapshotRecovery(body);
        }
        catch (Exception error) { primary = error; }
        return ReleaseSnapshotRecoveryOwners(server, service, monitor, source, clock, facts, body, callers, primary);
    }

    private static async Task RunSnapshotRecoveryBodyAsync(HealthPipeServer server, string pipe, SnapshotRecoverySource source,
        IConditionMonitor monitor, SnapshotRecoveryCallers callers, SnapshotRecoveryFacts facts)
    {
        await TransportTestSupport.StartServerAsync(server, pipe);
        Task<JsonObject> first = TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}");
        callers.Requests.Add(first);
        TransportTestSupport.ValidateError(await first, "ReportingUnavailable");
        source.Throw = false;
        Volatile.Write(ref facts.beforeRecoveryCaptures, source.Captures);
        Task<JsonObject> recovery = TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}");
        callers.Requests.Add(recovery);
        TransportTestSupport.ValidateSnapshotEnvelope(await recovery, "health", true, true);
        Assert.AreEqual(2, source.Captures); GC.KeepAlive(monitor);
    }

    private sealed class SnapshotRecoveryCallers
    {
        internal List<Task<JsonObject>> Requests { get; } = [];
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static SnapshotRecoveryObservation ReleaseSnapshotRecoveryOwners(HealthPipeServer? server,
        HealthcheckService service, IConditionMonitor monitor, SnapshotRecoverySource source, SnapshotRecoveryClock clock,
        SnapshotRecoveryFacts facts, Task? body, SnapshotRecoveryCallers callers, Exception? primary)
    {
        Task? stopping = server is null ? null : BeginSnapshotRecoveryStop(server);
        Task? disposing = server is null ? null : BeginSnapshotRecoveryDispose(server);
        var status = new SnapshotRecoveryStatus();
        var observers = new List<WeakReference<Task>>();
        if (stopping is not null) { observers.Add(ObserveSnapshotRecoveryTerminal(stopping, status, false)); }
        if (disposing is not null) { observers.Add(ObserveSnapshotRecoveryTerminal(disposing, status, true)); }
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
                        JoinSnapshotRecovery(actual);
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
            try { JoinSnapshotRecovery(monitor.DisposeAsync().AsTask()); Volatile.Write(ref facts.monitorJoined, 1); }
            catch (Exception error) { holds.Add(error); }
            try { JoinSnapshotRecovery(service.DisposeAsync().AsTask()); Volatile.Write(ref facts.serviceJoined, 1); }
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
            try { RequireSnapshotRecoveryPhase(facts); }
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
            return new(null, graph, facts, status, [.. observers]);
        }
        if (server is not null)
        {
            Task all = Task.WhenAll(stopping!, disposing!);
            try { JoinSnapshotRecovery(all); }
            catch (Exception error) { holds.Add(all.Exception ?? error); }
            foreach (WeakReference<Task> observer in observers)
            {
                try { if (observer.TryGetTarget(out Task? actual)) { JoinSnapshotRecovery(actual); } }
                catch (Exception error) { holds.Add(error); }
            }
            holds.AddRange(status.Errors());
        }
        if (holds.Count > 0)
        {
            if (primary is not null) { holds.Insert(0, primary); }
            primary = new AggregateException("The original intentional-source-error recovery body or its actual resource cleanup did not settle.", holds);
        }
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), [], facts, null, []);
    }

    private static void ReviewSnapshotRecoveryScenario(SnapshotRecoveryObservation observed)
    {
        if (observed.Graph.Length > 0)
        {
            var elapsed = Stopwatch.StartNew();
            while (observed.Graph.Any(reference => reference.IsAlive) && elapsed.Elapsed < SnapshotRecoveryDeadline)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                if (observed.Graph.Any(reference => reference.IsAlive) && elapsed.Elapsed < SnapshotRecoveryDeadline) { Thread.Sleep(1); }
            }
            if (observed.Graph.Any(reference => reference.IsAlive))
            {
                throw new InvalidOperationException("Actual accepting/request/source/clock/timer ownership remains live; elapsed time cannot certify settlement.");
            }
            RequireSnapshotRecoveryPhase(observed.Facts);
            foreach (WeakReference<Task> reference in observed.Observers)
            {
                if (reference.TryGetTarget(out Task? actual)) { JoinSnapshotRecovery(actual); }
            }
            Exception[] errors = observed.Status!.Errors();
            if (errors.Length > 0) { throw new AggregateException("An actual uncanceled terminal operation failed.", errors); }
        }
        observed.Primary?.Throw();
        if (observed.Graph.Length > 0)
        {
            Assert.IsTrue(observed.Status!.StopCompleted, "Actual terminal Stop must publish success after the real intentional-source-error/recovery exchanges, timers and all queued workers settle.");
            Assert.IsTrue(observed.Status.DisposeCompleted, "Actual terminal Dispose must publish success after the real intentional-source-error/recovery exchanges, timers and all queued workers settle.");
        }
    }

    private static void RequireSnapshotRecoveryPhase(SnapshotRecoveryFacts facts)
    {
        Exception[] errors = facts.Errors();
        if (errors.Length > 0) { throw new AggregateException("An actual unknown source, clock or timer callback failure remains a resource hold.", errors); }
        if (!facts.OriginalFirstThrowProven || Volatile.Read(ref facts.captures) != 2 || Volatile.Read(ref facts.returns) != 2
            || Volatile.Read(ref facts.beforeRecoveryCaptures) != 1
            || Volatile.Read(ref facts.createdTimers) != 2 || Volatile.Read(ref facts.actualDrainsJoined) != 2
            || Volatile.Read(ref facts.callbacksEntered) != Volatile.Read(ref facts.callbacksReturned)
            || Volatile.Read(ref facts.clientTasksJoined) != 2 || Volatile.Read(ref facts.bodyJoined) != 1
            || Volatile.Read(ref facts.monitorJoined) != 1 || Volatile.Read(ref facts.serviceJoined) != 1)
        {
            throw new InvalidOperationException("The original first-capture producer error must match its sole creation object/site/count, both actual requests/timer drains and source returns, later healthy recovery and actual state disposals; unknown phases remain holds.");
        }
    }
    private static void JoinSnapshotRecovery(Task actual) => actual.WaitAsync(SnapshotRecoveryDeadline).GetAwaiter().GetResult();
    private static T JoinSnapshotRecovery<T>(Task<T> actual) => actual.WaitAsync(SnapshotRecoveryDeadline).GetAwaiter().GetResult();
    private static Task BeginSnapshotRecoveryStop(HealthPipeServer server)
    {
        try { return server.StopAsync(); }
        catch (Exception error) { return Task.FromException(error); }
    }
    private static Task BeginSnapshotRecoveryDispose(HealthPipeServer server)
    {
        try { return server.DisposeAsync().AsTask(); }
        catch (Exception error) { return Task.FromException(error); }
    }
    private static WeakReference<Task> ObserveSnapshotRecoveryTerminal(Task actual, SnapshotRecoveryStatus status, bool disposal)
    {
        Task observer = actual.ContinueWith(static (completed, state) =>
        {
            var observed = ((SnapshotRecoveryStatus Status, bool Disposal))state!;
            if (completed.Exception is { } error) { observed.Status.RecordError(error); }
            else if (completed.IsCanceled) { observed.Status.RecordError(new TaskCanceledException("Actual uncanceled terminal operation canceled.")); }
            else { observed.Status.RecordSuccess(observed.Disposal); }
        }, (status, disposal), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new(observer);
    }
    private sealed record SnapshotRecoveryObservation(ExceptionDispatchInfo? Primary, WeakReference[] Graph,
        SnapshotRecoveryFacts Facts, SnapshotRecoveryStatus? Status, WeakReference<Task>[] Observers);
    private sealed class SnapshotRecoveryStatus
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
    private sealed class SnapshotRecoveryFacts
    {
        private readonly object gate = new();
        private readonly List<Exception> errors = [];
        private Exception? originalFirstError;
        private int originalCreations, originalMatches, originalCaptureOrdinal;
        private string? originalCreationSite;
        internal int captures, returns, createdTimers, actualDrainsJoined, callbacksEntered, callbacksReturned;
        internal int clientTasksJoined, bodyJoined, beforeRecoveryCaptures = -1, monitorJoined, serviceJoined;
        internal void RecordError(Exception error) { lock (gate) { errors.Add(error); } }
        internal Exception[] Errors() { lock (gate) { return [.. errors]; } }
        internal Exception? OriginalFirstError { get { lock (gate) { return originalFirstError; } } }
        internal bool OriginalFirstThrowProven
        {
            get { lock (gate) { return originalFirstError is not null && originalCreations == 1 && originalMatches == 1 && originalCaptureOrdinal == 1 && originalCreationSite == "SnapshotRecoverySource.CaptureSnapshot.Throw"; } }
        }
        internal void RecordOriginalCreation(Exception actualCreatedHere, int captureOrdinal)
        {
            lock (gate)
            {
                originalCreations++;
                if (originalCreations == 1 && captureOrdinal == 1)
                {
                    originalFirstError = actualCreatedHere;
                    originalCaptureOrdinal = captureOrdinal;
                    originalCreationSite = "SnapshotRecoverySource.CaptureSnapshot.Throw";
                }
            }
        }
        internal bool MatchOriginalFirstThrow(Exception actualCaught, Exception? actualCreatedHere, int captureOrdinal)
        {
            lock (gate)
            {
                if (captureOrdinal != 1 || originalCaptureOrdinal != 1 || originalCreations != 1 || originalMatches != 0
                    || originalCreationSite != "SnapshotRecoverySource.CaptureSnapshot.Throw"
                    || !ReferenceEquals(actualCaught, actualCreatedHere) || !ReferenceEquals(actualCaught, originalFirstError)) { return false; }
                originalMatches++;
                return true;
            }
        }
    }
    private sealed class SnapshotRecoverySource(HealthcheckService service, SnapshotRecoveryFacts facts) : IHealthSnapshotSource
    {
        private int throwing;
        internal bool Throw { get => Volatile.Read(ref throwing) != 0; set => Volatile.Write(ref throwing, value ? 1 : 0); }
        internal int Captures => Volatile.Read(ref facts.captures);
        public HealthSnapshot CaptureSnapshot()
        {
            int ordinal = Interlocked.Increment(ref facts.captures);
            Exception? actualCreatedHere = null;
            try
            {
                if (Throw)
                {
                    // The original fixture's intentional producer leaf. Only
                    // this very object at capture1 may match its first error.
                    actualCreatedHere = new InvalidOperationException("secret-source-error");
                    facts.RecordOriginalCreation(actualCreatedHere, ordinal);
                    throw actualCreatedHere;
                }
                return service.CaptureSnapshot();
            }
            catch (Exception error)
            {
                if (!facts.MatchOriginalFirstThrow(error, actualCreatedHere, ordinal)) { facts.RecordError(error); }
                throw;
            }
            finally { Interlocked.Increment(ref facts.returns); }
        }
    }
    private sealed class SnapshotRecoveryClock(SnapshotRecoveryFacts facts) : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<SnapshotRecoveryTimer> timers = [];
        public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;
        public override long GetTimestamp() => TimeProvider.System.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            try
            {
                var observed = new SnapshotRecoveryTimer(facts, callback, state);
                observed.Publish(TimeProvider.System.CreateTimer(observed.Invoke, null, dueTime, period));
                lock (gate) { timers.Add(observed); }
                Interlocked.Increment(ref facts.createdTimers);
                return observed;
            }
            catch (Exception error) { facts.RecordError(error); throw; }
        }
        internal void JoinActualDisposals()
        {
            SnapshotRecoveryTimer[] actual; lock (gate) { actual = [.. timers]; }
            var errors = new List<Exception>();
            foreach (SnapshotRecoveryTimer timer in actual)
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
        private sealed class SnapshotRecoveryTimer(SnapshotRecoveryFacts facts, TimerCallback callback, object? state) : ITimer
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
                ITimer timer; lock (gate) { timer = actual ?? throw new ObjectDisposedException(nameof(SnapshotRecoveryTimer)); }
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
                JoinSnapshotRecovery(disposalRequested.Task);
                Task drain; lock (gate) { drain = actualDisposal ?? throw new InvalidOperationException("Actual System disposal task missing."); }
                JoinSnapshotRecovery(drain);
                lock (gate) { observedCallback = null; observedState = null; actual = null; }
            }
        }
    }
}

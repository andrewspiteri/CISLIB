using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Healthcheck.Reporting;

public sealed partial class HealthcheckPipeTransportTests
{
    private static readonly TimeSpan SourceFailureDeadline = TransportTestSupport.Limit;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static SourceFailureObservation RunSourceFailureScenario(string failure, HealthcheckService? observedService = null, IConditionMonitor? observedMonitor = null)
    {
        if (failure is not ("cancellation" or "format")) { throw new ArgumentOutOfRangeException(nameof(failure)); }
        if ((observedService is null) != (observedMonitor is null)) { throw new ArgumentException("Both actual control owners must be supplied together."); }
        var (service, monitor) = observedService is null ? Create() : (observedService, observedMonitor!);
        string pipe = TransportTestSupport.NewPipeName();
        var facts = new SourceFailureFacts();
        // The original row-specific creation expression always runs here, even
        // in actual BODY controls. No arbitrary injected error can register known.
        Exception error = failure == "cancellation" ? new OperationCanceledException("private-source") : new FormatException("private-source");
        facts.RecordOriginalCreation(error, failure);
        var source = new SourceFailureSource(service, error, facts);
        var clock = new SourceFailureClock(facts);
        var callers = new SourceFailureCallers();
        HealthPipeServer? server = null;
        Task? body = null;
        Exception? primary = null;
        try
        {
            server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe }, clock);
            body = RunSourceFailureBodyAsync(server, pipe, source, monitor, callers, facts);
            JoinSourceFailure(body);
        }
        catch (Exception actual) { primary = actual; }
        return ReleaseSourceFailureOwners(server, service, monitor, source, clock, facts, body, callers, primary);
    }

    private static async Task RunSourceFailureBodyAsync(HealthPipeServer server, string pipe, SourceFailureSource source,
        IConditionMonitor monitor, SourceFailureCallers callers, SourceFailureFacts facts)
    {
        await TransportTestSupport.StartServerAsync(server, pipe);
        Task<JsonObject> first = TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}");
        callers.Requests.Add(first);
        TransportTestSupport.ValidateError(await first, "ReportingUnavailable");
        Volatile.Write(ref facts.beforeRecoveryCaptures, source.Captures);
        Task<JsonObject> recovery = TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}");
        callers.Requests.Add(recovery);
        TransportTestSupport.ValidateSnapshotEnvelope(await recovery, "health", true, true);
        Assert.AreEqual(2, source.Captures);
        GC.KeepAlive(monitor);
    }

    private sealed class SourceFailureCallers
    {
        internal List<Task<JsonObject>> Requests { get; } = [];
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static SourceFailureObservation ReleaseSourceFailureOwners(HealthPipeServer? server,
        HealthcheckService service, IConditionMonitor monitor, SourceFailureSource source, SourceFailureClock clock,
        SourceFailureFacts facts, Task? body, SourceFailureCallers callers, Exception? primary)
    {
        Task? stopping = server is null ? null : BeginSourceFailureStop(server);
        Task? disposing = server is null ? null : BeginSourceFailureDispose(server);
        var status = new SourceFailureStatus();
        var observers = new List<WeakReference<Task>>();
        if (stopping is not null) { observers.Add(ObserveSourceFailureTerminal(stopping, status, false)); }
        if (disposing is not null) { observers.Add(ObserveSourceFailureTerminal(disposing, status, true)); }
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
                        JoinSourceFailure(actual);
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
            try { JoinSourceFailure(monitor.DisposeAsync().AsTask()); Volatile.Write(ref facts.monitorJoined, 1); }
            catch (Exception error) { holds.Add(error); }
            try { JoinSourceFailure(service.DisposeAsync().AsTask()); Volatile.Write(ref facts.serviceJoined, 1); }
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
            try { RequireSourceFailurePhase(facts); }
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
            try { JoinSourceFailure(all); }
            catch (Exception error) { holds.Add(all.Exception ?? error); }
            foreach (WeakReference<Task> observer in observers)
            {
                try { if (observer.TryGetTarget(out Task? actual)) { JoinSourceFailure(actual); } }
                catch (Exception error) { holds.Add(error); }
            }
            holds.AddRange(status.Errors());
        }
        if (holds.Count > 0)
        {
            if (primary is not null) { holds.Insert(0, primary); }
            primary = new AggregateException("The original cancellation/format-source recovery body or its actual resource cleanup did not settle.", holds);
        }
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), [], facts, null, []);
    }

    private static void ReviewSourceFailureScenario(SourceFailureObservation observed)
    {
        if (observed.Graph.Length > 0)
        {
            var elapsed = Stopwatch.StartNew();
            while (observed.Graph.Any(reference => reference.IsAlive) && elapsed.Elapsed < SourceFailureDeadline)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                if (observed.Graph.Any(reference => reference.IsAlive) && elapsed.Elapsed < SourceFailureDeadline) { Thread.Sleep(1); }
            }
            if (observed.Graph.Any(reference => reference.IsAlive))
            {
                throw new InvalidOperationException("Actual accepting/request/source/clock/timer ownership remains live; elapsed time cannot certify settlement.");
            }
            RequireSourceFailurePhase(observed.Facts);
            foreach (WeakReference<Task> reference in observed.Observers)
            {
                if (reference.TryGetTarget(out Task? actual)) { JoinSourceFailure(actual); }
            }
            Exception[] errors = observed.Status!.Errors();
            if (errors.Length > 0) { throw new AggregateException("An actual uncanceled terminal operation failed.", errors); }
        }
        observed.Primary?.Throw();
        if (observed.Graph.Length > 0)
        {
            Assert.IsTrue(observed.Status!.StopCompleted, "Actual terminal Stop must publish success after the real cancellation/format-source recovery exchanges, timers and all queued workers settle.");
            Assert.IsTrue(observed.Status.DisposeCompleted, "Actual terminal Dispose must publish success after the real cancellation/format-source recovery exchanges, timers and all queued workers settle.");
        }
    }

    private static void RequireSourceFailurePhase(SourceFailureFacts facts)
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
    private static void JoinSourceFailure(Task actual) => actual.WaitAsync(SourceFailureDeadline).GetAwaiter().GetResult();
    private static T JoinSourceFailure<T>(Task<T> actual) => actual.WaitAsync(SourceFailureDeadline).GetAwaiter().GetResult();
    private static Task BeginSourceFailureStop(HealthPipeServer server)
    {
        try { return server.StopAsync(); }
        catch (Exception error) { return Task.FromException(error); }
    }
    private static Task BeginSourceFailureDispose(HealthPipeServer server)
    {
        try { return server.DisposeAsync().AsTask(); }
        catch (Exception error) { return Task.FromException(error); }
    }
    private static WeakReference<Task> ObserveSourceFailureTerminal(Task actual, SourceFailureStatus status, bool disposal)
    {
        Task observer = actual.ContinueWith(static (completed, state) =>
        {
            var observed = ((SourceFailureStatus Status, bool Disposal))state!;
            if (completed.Exception is { } error) { observed.Status.RecordError(error); }
            else if (completed.IsCanceled) { observed.Status.RecordError(new TaskCanceledException("Actual uncanceled terminal operation canceled.")); }
            else { observed.Status.RecordSuccess(observed.Disposal); }
        }, (status, disposal), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new(observer);
    }
    private sealed record SourceFailureObservation(ExceptionDispatchInfo? Primary, WeakReference[] Graph,
        SourceFailureFacts Facts, SourceFailureStatus? Status, WeakReference<Task>[] Observers);
    private sealed class SourceFailureStatus
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
    private sealed class SourceFailureFacts
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
            get
            {
                lock (gate)
                {
                    return originalFirstError is not null && originalCreations == 1 && originalMatches == 1 && originalCaptureOrdinal == 1
                        && originalCreationSite is "RunSourceFailureScenario.cancellation-expression" or "RunSourceFailureScenario.format-expression";
                }
            }
        }
        internal void RecordOriginalCreation(Exception actualCreatedAtOriginalExpression, string row)
        {
            lock (gate)
            {
                originalCreations++;
                if (originalCreations == 1 && row is "cancellation" or "format")
                {
                    originalFirstError = actualCreatedAtOriginalExpression;
                    originalCreationSite = row == "cancellation" ? "RunSourceFailureScenario.cancellation-expression" : "RunSourceFailureScenario.format-expression";
                }
            }
        }
        internal bool MatchOriginalFirstThrow(Exception actualCaught, Exception actualReadonlyFirstError, int captureOrdinal)
        {
            lock (gate)
            {
                if (captureOrdinal != 1 || originalCreations != 1 || originalMatches != 0
                    || originalCreationSite is not ("RunSourceFailureScenario.cancellation-expression" or "RunSourceFailureScenario.format-expression")
                    || !ReferenceEquals(actualCaught, actualReadonlyFirstError) || !ReferenceEquals(actualCaught, originalFirstError)) { return false; }
                originalMatches++;
                originalCaptureOrdinal = captureOrdinal;
                return true;
            }
        }
    }
    private sealed class SourceFailureSource : IHealthSnapshotSource
    {
        private readonly HealthcheckService service;
        private readonly Exception originalError;
        private readonly SourceFailureFacts facts;
        internal SourceFailureSource(HealthcheckService service, Exception originalError, SourceFailureFacts facts)
        {
            this.service = service;
            this.originalError = originalError;
            this.facts = facts;
        }
        internal int Captures => Volatile.Read(ref facts.captures);
        public HealthSnapshot CaptureSnapshot()
        {
            int ordinal = Interlocked.Increment(ref facts.captures);
            try
            {
                if (ordinal == 1) { throw originalError; }
                return service.CaptureSnapshot();
            }
            catch (Exception error)
            {
                if (!facts.MatchOriginalFirstThrow(error, originalError, ordinal)) { facts.RecordError(error); }
                throw;
            }
            finally { Interlocked.Increment(ref facts.returns); }
        }
    }
    private sealed class SourceFailureClock(SourceFailureFacts facts) : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<SourceFailureTimer> timers = [];
        public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;
        public override long GetTimestamp() => TimeProvider.System.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            try
            {
                var observed = new SourceFailureTimer(facts, callback, state);
                observed.Publish(TimeProvider.System.CreateTimer(observed.Invoke, null, dueTime, period));
                lock (gate) { timers.Add(observed); }
                Interlocked.Increment(ref facts.createdTimers);
                return observed;
            }
            catch (Exception error) { facts.RecordError(error); throw; }
        }
        internal void JoinActualDisposals()
        {
            SourceFailureTimer[] actual; lock (gate) { actual = [.. timers]; }
            var errors = new List<Exception>();
            foreach (SourceFailureTimer timer in actual)
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
        private sealed class SourceFailureTimer(SourceFailureFacts facts, TimerCallback callback, object? state) : ITimer
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
                ITimer timer; lock (gate) { timer = actual ?? throw new ObjectDisposedException(nameof(SourceFailureTimer)); }
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
                JoinSourceFailure(disposalRequested.Task);
                Task drain; lock (gate) { drain = actualDisposal ?? throw new InvalidOperationException("Actual System disposal task missing."); }
                JoinSourceFailure(drain);
                lock (gate) { observedCallback = null; observedState = null; actual = null; }
            }
        }
    }
}

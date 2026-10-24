using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Healthcheck.Reporting;

public sealed partial class HealthcheckPipeTransportTests
{
    private static readonly TimeSpan IntegrationStopDeadline = TransportTestSupport.Limit;

    // The original one-row scenario owns all strong server/request references
    // in this synchronous frame. No public async state machine owns its server.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IntegrationStopObservation RunStopCancellationScenario()
    {
        var (service, monitor) = Create();
        string pipe = TransportTestSupport.NewPipeName();
        using var release = new ManualResetEventSlim();
        var source = new IntegrationStopSource(service, release);
        var clock = new IntegrationStopClock();
        HealthPipeServer? server = null;
        Task<JsonObject>? request = null;
        Task? originalStop = null;
        Exception? primary = null;
        try
        {
            // Preserve the original explicit one-slot setting and every
            // default timeout value. The clock delegates real System timers.
            server = new HealthPipeServer(source,
                new HealthcheckOptions { PipeEnabled = true, PipeName = pipe, PipeMaxClients = 1 }, clock);
            JoinIntegrationStop(TransportTestSupport.StartServerAsync(server, pipe));
            request = TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}");
            JoinIntegrationStop(source.Entered);
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            JoinIntegrationStop(Assert.ThrowsAsync<OperationCanceledException>(() => server.StopAsync(canceled.Token)));
            originalStop = server.StopAsync();
            Assert.IsFalse(originalStop.IsCompleted, "Stop must include actual source settlement.");
            Assert.IsFalse(release.IsSet, "The source's actual release gate is still closed at the pending-Stop milestone.");
            release.Set();
            // Original Stop success is asserted only after independently joined
            // actual request/source/timer owners and actual queued-owner death.
            try
            {
                _ = JoinIntegrationStop(request);
                Assert.Fail("Closed stop admission must not produce a successful pending reply.");
            }
            catch (IOException) { }
            catch (OperationCanceledException) { }
        }
        catch (Exception error) { primary = error; }
        return ReleaseIntegrationStopOwners(server, service, monitor, source, clock, release, request, originalStop, primary);
    }

    private static IntegrationStopObservation ReleaseIntegrationStopOwners(HealthPipeServer? server,
        HealthcheckService service, IConditionMonitor monitor, IntegrationStopSource source, IntegrationStopClock clock,
        ManualResetEventSlim release, Task<JsonObject>? request, Task? originalStop, Exception? primary)
    {
        Task? stopping = server is null ? null : BeginIntegrationStop(server);
        Task? disposing = server is null ? null : BeginIntegrationDispose(server);
        var status = new IntegrationStopStatus();
        var observers = new List<WeakReference<Task>>();
        if (stopping is not null) { observers.Add(ObserveIntegrationStop(stopping, status, 0)); }
        if (disposing is not null) { observers.Add(ObserveIntegrationStop(disposing, status, 1)); }
        if (originalStop is not null) { observers.Add(ObserveIntegrationStop(originalStop, status, 2)); }
        var holds = new List<Exception>();
        bool requestJoined = false, sourceJoined = false;
        try
        {
            release.Set();
            if (request is not null)
            {
                // RequestAsync's actual Task includes its real frame read and
                // await-using client disposal. No caller-owned server reference
                // is captured in that unchanged static helper.
                try { _ = JoinIntegrationStop(request); }
                catch (IOException) { }
                catch (OperationCanceledException) { }
                requestJoined = true;
            }
            if (source.Entered.IsCompletedSuccessfully)
            {
                JoinIntegrationStop(source.Returned);
                clock.JoinActualDisposal();
                RequireIntegrationStopPhase(source, clock);
                sourceJoined = true;
            }
        }
        catch (Exception error) { holds.Add(error); }
        finally
        {
            try { JoinIntegrationStop(monitor.DisposeAsync().AsTask()); }
            catch (Exception error) { holds.Add(error); }
            try { JoinIntegrationStop(service.DisposeAsync().AsTask()); }
            catch (Exception error) { holds.Add(error); }
            GC.KeepAlive(monitor);
        }
        bool eligible = server is not null && originalStop is not null && requestJoined && sourceJoined && holds.Count == 0
            && !stopping!.IsFaulted && !stopping.IsCanceled && !disposing!.IsFaulted && !disposing.IsCanceled
            && !originalStop.IsFaulted && !originalStop.IsCanceled;
        if (eligible)
        {
            // Only the exact one-slot remaining-decrement/final-progress source
            // family is reviewed here. BeginStop still closes published streams;
            // actual RunSlot/HandleClient work retains the server through IO,
            // the source tail, timer disposal and actual finally.
            return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), new(server!), source, clock, status, [.. observers]);
        }
        if (server is not null)
        {
            Task all = originalStop is null ? Task.WhenAll(stopping!, disposing!) : Task.WhenAll(stopping!, disposing!, originalStop);
            try { JoinIntegrationStop(all); }
            catch (Exception error) { holds.Add(all.Exception ?? error); }
            foreach (WeakReference<Task> observer in observers)
            {
                try { if (observer.TryGetTarget(out Task? actual)) { JoinIntegrationStop(actual); } }
                catch (Exception error) { holds.Add(error); }
            }
            holds.AddRange(status.Errors());
        }
        if (holds.Count > 0)
        {
            if (primary is not null) { holds.Insert(0, primary); }
            primary = new AggregateException("The original Integration scenario or its actual resource cleanup did not settle.", holds);
        }
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), null, null, null, null, []);
    }

    private static void ReviewStopCancellationScenario(IntegrationStopObservation observed)
    {
        if (observed.Owner is { } owner)
        {
            var elapsed = Stopwatch.StartNew();
            while (owner.IsAlive && elapsed.Elapsed < IntegrationStopDeadline)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                if (owner.IsAlive && elapsed.Elapsed < IntegrationStopDeadline) { Thread.Sleep(1); }
            }
            if (owner.IsAlive) { ThrowIntegrationStopHold(observed.Primary, new InvalidOperationException("Actual accepting/request/IO ownership remains live; elapsed time cannot certify settlement.")); }
            try
            {
                RequireIntegrationStopPhase(observed.Source!, observed.Clock!);
                foreach (WeakReference<Task> reference in observed.Observers)
                {
                    if (reference.TryGetTarget(out Task? actualObserver)) { JoinIntegrationStop(actualObserver); }
                }
                Exception[] errors = observed.Status!.Errors();
                if (errors.Length > 0) { throw new AggregateException("Actual original terminal operation failed.", errors); }
            }
            catch (Exception hold) { ThrowIntegrationStopHold(observed.Primary, hold); }
        }
        observed.Primary?.Throw();
        if (observed.Owner is not null)
        {
            Assert.IsTrue(observed.Status!.OriginalStopCompleted, "The original uncanceled Stop must publish success after the real source/request/timer and queued owners settle.");
            Assert.IsTrue(observed.Status.StopCompleted, "Actual terminal Stop must complete successfully after actual owned settlement.");
            Assert.IsTrue(observed.Status.DisposeCompleted, "Actual terminal Dispose must complete successfully after actual owned settlement.");
        }
    }

    private static void RequireIntegrationStopPhase(IntegrationStopSource source, IntegrationStopClock clock)
    {
        Exception[] errors = source.Errors();
        if (errors.Length > 0) { throw new AggregateException("An actual source callback failure remains a resource hold.", errors); }
        if (clock.ActualCallbackFailure is { } timerFailure) { throw new AggregateException("An actual System timer callback failure remains a resource hold.", timerFailure); }
        if (source.Captures != 1 || source.Returns != 1 || !source.Returned.IsCompletedSuccessfully
            || clock.Created != 1 || clock.ActualDrainsJoined != 1 || clock.CallbacksEntered != clock.CallbacksReturned)
        {
            throw new InvalidOperationException("Exactly one actual source return, client/request join and real System timer drain are required; unknown phases remain holds.");
        }
    }
    private static void ThrowIntegrationStopHold(ExceptionDispatchInfo? primary, Exception hold)
    {
        if (primary is not null) { throw new AggregateException("The original body and actual cleanup both failed.", primary.SourceException, hold); }
        ExceptionDispatchInfo.Capture(hold).Throw();
    }
    private static void JoinIntegrationStop(Task actual) => actual.WaitAsync(IntegrationStopDeadline).GetAwaiter().GetResult();
    private static T JoinIntegrationStop<T>(Task<T> actual) => actual.WaitAsync(IntegrationStopDeadline).GetAwaiter().GetResult();
    private static Task BeginIntegrationStop(HealthPipeServer server)
    {
        try { return server.StopAsync(); }
        catch (Exception error) { return Task.FromException(error); }
    }
    private static Task BeginIntegrationDispose(HealthPipeServer server)
    {
        try { return server.DisposeAsync().AsTask(); }
        catch (Exception error) { return Task.FromException(error); }
    }
    private static WeakReference<Task> ObserveIntegrationStop(Task actual, IntegrationStopStatus status, int kind)
    {
        Task observer = actual.ContinueWith(static (completed, state) =>
        {
            var observed = ((IntegrationStopStatus Status, int Kind))state!;
            if (completed.Exception is { } error) { observed.Status.RecordError(error); }
            else if (completed.IsCanceled) { observed.Status.RecordError(new TaskCanceledException("Actual uncanceled terminal operation canceled.")); }
            else { observed.Status.RecordSuccess(observed.Kind); }
        }, (status, kind), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new(observer);
    }
    private sealed record IntegrationStopObservation(ExceptionDispatchInfo? Primary, WeakReference? Owner,
        IntegrationStopSource? Source, IntegrationStopClock? Clock, IntegrationStopStatus? Status, WeakReference<Task>[] Observers);
    private sealed class IntegrationStopStatus
    {
        private readonly object gate = new();
        private readonly List<Exception> errors = [];
        private bool stop, dispose, original;
        internal bool StopCompleted { get { lock (gate) { return stop; } } }
        internal bool DisposeCompleted { get { lock (gate) { return dispose; } } }
        internal bool OriginalStopCompleted { get { lock (gate) { return original; } } }
        internal void RecordSuccess(int kind) { lock (gate) { if (kind == 0) { stop = true; } else if (kind == 1) { dispose = true; } else { original = true; } } }
        internal void RecordError(Exception error) { lock (gate) { errors.Add(error); } }
        internal Exception[] Errors() { lock (gate) { return [.. errors]; } }
    }
    private sealed class IntegrationStopSource(HealthcheckService service, ManualResetEventSlim release) : IHealthSnapshotSource
    {
        private readonly object gate = new();
        private readonly List<Exception> errors = [];
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource returned = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int captures, returns;
        internal Task Entered => entered.Task;
        internal Task Returned => returned.Task;
        internal int Captures => Volatile.Read(ref captures);
        internal int Returns => Volatile.Read(ref returns);
        internal Exception[] Errors() { lock (gate) { return [.. errors]; } }
        public HealthSnapshot CaptureSnapshot()
        {
            Interlocked.Increment(ref captures); entered.TrySetResult();
            try
            {
                if (!release.Wait(IntegrationStopDeadline)) { throw new TimeoutException("The actual source gate was not released within its original source wait bound."); }
                return service.CaptureSnapshot();
            }
            catch (Exception error) { lock (gate) { errors.Add(error); } throw; }
            finally { Interlocked.Increment(ref returns); returned.TrySetResult(); }
        }
    }
    private sealed class IntegrationStopClock : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<IntegrationStopTimer> timers = [];
        private int created, actualDrainsJoined, callbacksEntered, callbacksReturned;
        private Exception? actualCallbackFailure;
        internal int Created => Volatile.Read(ref created);
        internal int ActualDrainsJoined => Volatile.Read(ref actualDrainsJoined);
        internal int CallbacksEntered => Volatile.Read(ref callbacksEntered);
        internal int CallbacksReturned => Volatile.Read(ref callbacksReturned);
        internal Exception? ActualCallbackFailure => Volatile.Read(ref actualCallbackFailure);
        public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;
        public override long GetTimestamp() => TimeProvider.System.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var observed = new IntegrationStopTimer(this, callback, state);
            observed.Publish(TimeProvider.System.CreateTimer(observed.Invoke, null, dueTime, period));
            lock (gate) { timers.Add(observed); }
            Interlocked.Increment(ref created);
            return observed;
        }
        internal void JoinActualDisposal()
        {
            IntegrationStopTimer[] actual; lock (gate) { actual = [.. timers]; }
            if (actual.Length != 1 || Created != 1) { throw new InvalidOperationException("One actual original request timer is required."); }
            actual[0].JoinActualAndReleaseOnlyObserverReferences();
            Interlocked.Increment(ref actualDrainsJoined);
        }
        private sealed class IntegrationStopTimer(IntegrationStopClock clock, TimerCallback callback, object? state) : ITimer
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
                Interlocked.Increment(ref clock.callbacksEntered);
                try
                {
                    TimerCallback current; object? currentState;
                    lock (gate) { current = observedCallback ?? throw new InvalidOperationException("Callback entered after its actual timer drain."); currentState = observedState; }
                    current(currentState);
                }
                catch (Exception error) { Interlocked.CompareExchange(ref clock.actualCallbackFailure, error, null); throw; }
                finally { Interlocked.Increment(ref clock.callbacksReturned); }
            }
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                ITimer timer; lock (gate) { timer = actual ?? throw new ObjectDisposedException(nameof(IntegrationStopTimer)); }
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
                // Never join inside Dispose; it may be called from this actual
                // callback, whose underlying DisposeAsync task awaits its return.
                disposalRequested.TrySetResult();
            }
            public ValueTask DisposeAsync() { Dispose(); lock (gate) { return new(actualDisposal!); } }
            internal void JoinActualAndReleaseOnlyObserverReferences()
            {
                JoinIntegrationStop(disposalRequested.Task);
                Task drain; lock (gate) { drain = actualDisposal ?? throw new InvalidOperationException("Actual System disposal task missing."); }
                JoinIntegrationStop(drain);
                // Keep the exact actual joined task for idempotent disposal;
                // clear only observer-owned refs after real callback drain.
                lock (gate) { observedCallback = null; observedState = null; actual = null; }
            }
        }
    }
}

using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

public sealed partial class HealthProbeCallerCompletionQualificationTests
{
    private static HealthPipeServer CreateServerWhileProbeIsPending(ProbeStartupSource source,
        HealthcheckOptions options, ProbeStartupClock clock, QueuedCallerContext context,
        Task<int> running, CancellationTokenSource callerCancellation)
    {
        StartupObservation observed = StartWithActualPendingProbeAndReleaseFailure(
            source, options, clock, context, running, callerCancellation);
        if (observed.Started is { } started) { return started; }
        if (observed.FailedStartupOwner is { } owner)
        {
            CollectFailedStartupOwner(owner);
            if (owner.IsAlive)
            {
                throw new AggregateException("Enabled startup failed and its actual queued/accepting owner remains live.",
                    observed.Primary!.SourceException,
                    new InvalidOperationException("Unsettled actual IO or queued work is a cleanup hold; elapsed time does not certify settlement."));
            }
            try
            {
                // The client existed before startup. A worker could have passed
                // the cancellation check just before closure; read the phase
                // counters again only after its actual owner has disappeared.
                RequireNoServerRequestPhase(source, clock);
                foreach (WeakReference<Task> reference in observed.CleanupObservers)
                {
                    if (reference.TryGetTarget(out Task? actualObserver)) { JoinStartupOperation(actualObserver); }
                }
                Exception[] failures = observed.Cleanup!.Failures();
                if (failures.Length > 0) { throw new AggregateException("Actual failed-start cleanup failed.", failures); }
            }
            catch (Exception error)
            {
                throw new AggregateException("The enabled-startup assertion and actual cleanup both failed.",
                    observed.Primary!.SourceException, error);
            }
        }
        observed.Primary!.Throw();
        throw new InvalidOperationException("A failed startup observation must retain its primary failure.");
    }

    // The caller's real probe/context is already pending before construction.
    // A successful server is handed over only after actual Start completes.
    // On failure this synchronous helper must cancel/drain/join that probe
    // before relinquishing its sole strong server owner.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static StartupObservation StartWithActualPendingProbeAndReleaseFailure(ProbeStartupSource source,
        HealthcheckOptions options, ProbeStartupClock clock, QueuedCallerContext context,
        Task<int> running, CancellationTokenSource callerCancellation)
    {
        var server = new HealthPipeServer(source, options, clock);
        Exception? primary = null;
        bool exactStartupQueueFailureObserved = false;
        try
        {
            Exception? startupFailure = null;
            string? ownedOriginalFault = null;
            Task? starting = null;
            try { starting = server.StartAsync(); }
            catch (NullReferenceException error)
            {
                try { ownedOriginalFault = ReadAndObserveOriginalQueueFault(server).ToString(); }
                catch (Exception observationFailure)
                {
                    throw new AggregateException("Synchronous startup failed and its original owned fault could not be safely observed.", error, observationFailure);
                }
                exactStartupQueueFailureObserved = true;
                startupFailure = error;
            }
            if (starting is not null)
            {
                try { JoinStartupOperation(starting); }
                catch (InvalidOperationException error) when (IsOriginalQueueFault(error))
                {
                    exactStartupQueueFailureObserved = true;
                    startupFailure = error;
                }
            }
            else if (startupFailure is null)
            {
                throw new InvalidOperationException("Successful startup did not return its actual wait task.");
            }
            Assert.IsNull(startupFailure, "Enabled startup of this fresh local endpoint must succeed. Actual startup failure:\n"
                + startupFailure + (ownedOriginalFault is null ? "" : "\nObserved original owned startup fault:\n" + ownedOriginalFault));
            return new(server, null, null, null, []);
        }
        catch (Exception error) { primary = error; }

        Task stopping = BeginStop(server);
        Task disposing = BeginDispose(server);
        var cleanup = new CleanupObservation();
        WeakReference<Task>[] observers = [ObserveActualCleanup(stopping, cleanup), ObserveActualCleanup(disposing, cleanup)];
        Task? drains = null;
        WeakReference? owner = null;
        try
        {
            callerCancellation.Cancel();
            JoinStartupOperation(context.DrainUntilCompletedAsync(running, Deadline));
            if (exactStartupQueueFailureObserved && primary is AssertFailedException)
            {
                // Source-specific boundary for the reviewed queue inversion,
                // never a generic inference from cancellation or public Stop.
                // Unlike no-client fixtures this actual caller has connected
                // or been canceled, so its real Task and queued callbacks must
                // settle before this negative phase predicate is considered.
                if (!options.PipeEnabled || options.PipeMaxClients != 1 || !running.IsCompletedSuccessfully
                    || stopping.IsFaulted || stopping.IsCanceled || disposing.IsFaulted || disposing.IsCanceled)
                {
                    throw new InvalidOperationException("Actual failed-start probe/terminal cleanup remains a runtime hold.");
                }
                RequireNoServerRequestPhase(source, clock);
                owner = new WeakReference(server);
            }
            else
            {
                drains = Task.WhenAll(stopping, disposing);
                JoinStartupOperation(drains);
                foreach (WeakReference<Task> reference in observers)
                {
                    if (reference.TryGetTarget(out Task? actualObserver)) { JoinStartupOperation(actualObserver); }
                }
                Exception[] failures = cleanup.Failures();
                if (failures.Length > 0) { throw new AggregateException("Actual failed-start cleanup failed.", failures); }
            }
        }
        catch (Exception error)
        {
            // Any crossed capture/timer phase stays an explicit hold even if
            // the mutated server's public drain notification completed early.
            Exception cleanupFailure = error;
            if (drains is null)
            {
                try
                {
                    drains = Task.WhenAll(stopping, disposing);
                    JoinStartupOperation(drains);
                }
                catch (Exception drainFailure)
                {
                    cleanupFailure = new AggregateException("The actual probe/phase and terminal cleanup both failed.", error, drains?.Exception ?? drainFailure);
                }
            }
            primary = new AggregateException("Startup and actual owned cleanup both failed.", primary!, cleanupFailure);
        }
        return new(null, ExceptionDispatchInfo.Capture(primary!), owner, cleanup, observers);
    }

    private static void RequireNoServerRequestPhase(ProbeStartupSource source, ProbeStartupClock clock)
    {
        if (source.Captures != 0 || clock.TimersCreated != 0)
        {
            throw new InvalidOperationException("The failed startup crossed an actual server capture/timer phase; request ownership remains a runtime/cleanup hold.");
        }
    }

    private static InvalidOperationException ReadAndObserveOriginalQueueFault(HealthPipeServer server)
    {
        // Cleanup-only read of the real owned fault. No writes or substitute
        // completion/task/worker invocation occurs.
        FieldInfo? field = typeof(HealthPipeServer).GetField("startCompletion", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.FieldType != typeof(TaskCompletionSource) || field.GetValue(server) is not TaskCompletionSource completion
            || !completion.Task.IsFaulted || completion.Task.Exception is not { } original)
        {
            throw new InvalidOperationException("The actual saved startup failure could not be observed; its unresolved runtime work remains a hold.");
        }
        Exception[] failures = original.Flatten().InnerExceptions.ToArray();
        if (failures.Length != 1 || failures[0] is not InvalidOperationException failure || !IsOriginalQueueFault(failure))
        {
            throw new AggregateException("Unexpected actual saved startup fault remains a runtime/cleanup hold.", failures);
        }
        return failure;
    }

    private static bool IsOriginalQueueFault(InvalidOperationException error) => error.GetType() == typeof(InvalidOperationException)
        && error.Message == "The Healthcheck pipe worker could not be queued."
        && error.TargetSite?.Name == "StartCore" && error.TargetSite?.DeclaringType == typeof(HealthPipeServer);

    private static Task BeginStop(HealthPipeServer server)
    {
        try { return server.StopAsync(); }
        catch (Exception error) { return Task.FromException(error); }
    }

    private static Task BeginDispose(HealthPipeServer server)
    {
        try { return server.DisposeAsync().AsTask(); }
        catch (Exception error) { return Task.FromException(error); }
    }

    private static void JoinStartupOperation(Task actual) => actual.WaitAsync(Deadline).GetAwaiter().GetResult();

    private static WeakReference<Task> ObserveActualCleanup(Task actual, CleanupObservation observation)
    {
        Task observer = actual.ContinueWith(static (completed, state) =>
        {
            var errors = (CleanupObservation)state!;
            if (completed.Exception is { } failure) { errors.Add(failure); }
            else if (completed.IsCanceled) { errors.Add(new TaskCanceledException("Actual startup cleanup was canceled.")); }
        }, observation, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new(observer);
    }

    private static void CollectFailedStartupOwner(WeakReference reference)
    {
        var observation = Stopwatch.StartNew();
        while (reference.IsAlive && observation.Elapsed < Deadline)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            if (reference.IsAlive && observation.Elapsed < Deadline) { Thread.Sleep(1); }
        }
    }

    private sealed record StartupObservation(HealthPipeServer? Started, ExceptionDispatchInfo? Primary,
        WeakReference? FailedStartupOwner, CleanupObservation? Cleanup, WeakReference<Task>[] CleanupObservers);

    private sealed class CleanupObservation
    {
        private readonly object gate = new();
        private readonly List<Exception> failures = [];
        internal void Add(Exception error) { lock (gate) { failures.Add(error); } }
        internal Exception[] Failures() { lock (gate) { return [.. failures]; } }
    }

    private sealed class ProbeStartupSource(HealthcheckService service) : IHealthSnapshotSource
    {
        internal PendingProbeSourcePhase Phase { get; } = new();
        internal int Captures => Volatile.Read(ref Phase.Captures);
        public HealthSnapshot CaptureSnapshot()
        {
            Interlocked.Increment(ref Phase.Captures);
            try { return service.CaptureSnapshot(); }
            catch (Exception error) { Phase.RecordFailure(error); throw; }
            finally { Interlocked.Increment(ref Phase.Exits); }
        }
    }

    private sealed class ProbeStartupClock : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<ObservedTimer> timers = [];
        internal PendingProbeClockPhase Phase { get; } = new();
        internal int TimersCreated => Volatile.Read(ref Phase.Created);
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

        private sealed class ObservedTimer(ProbeStartupClock clock, TimerCallback callback, object? state) : ITimer
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
                JoinPendingProbe(disposalRequested.Task);
                Task drain; lock (gate) { drain = actualDisposal ?? throw new InvalidOperationException("No actual System disposal Task was returned."); }
                JoinPendingProbe(drain);
                // Keep the exact actual completed Task for idempotent calls.
                lock (gate) { observedCallback = null; observedState = null; actual = null; }
            }
        }
    }
}

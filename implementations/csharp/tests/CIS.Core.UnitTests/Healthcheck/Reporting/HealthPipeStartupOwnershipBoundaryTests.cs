using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using CIS.Healthcheck;
using CIS.Healthcheck.Pipes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32.SafeHandles;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
// Forced GC and lifetime probes require isolation from unrelated tests.
[DoNotParallelize]
public sealed class HealthPipeStartupOwnershipBoundaryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan UnrequestedTerminalDeadline = TimeSpan.FromSeconds(3);

    [TestMethod]
    public void EnabledStartupSucceedsAndReleasesItsActualQueuedOwnerWhenStopped()
    {
        ReviewStartupObservation(StartStopAndRelease());
    }

    [TestMethod]
    public void StopDuringActualEndpointConstructionWaitsForStartupAndReleasesItsOwner()
    {
        ReviewStartupObservation(StartStopAndRelease(stopDuringConstruction: true));
    }

    [TestMethod]
    public void ARealSecondEndpointCreationFailureClosesTheFirstAndSettlesTerminalCalls()
    {
        ReviewStartupObservation(ObservePartialCreationFailure());
    }

    [TestMethod]
    public void StopDuringActualInitialCreationClosesTheUnpublishedHandleBeforeCollection()
    {
        ReviewStartupObservation(ObserveUnpublishedInitialHandle());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static StartupObservation ObservePartialCreationFailure()
    {
        var source = new UnrequestedSource();
        var clock = new NoRequestClock();
        NamedPipeServerStream? firstEndpoint = null;
        SafePipeHandle? firstHandle = null;
        int attempts = 0;
        string pipeName = "hc-partial-start-" + Guid.NewGuid().ToString("N");
        const int maximumClients = 2;
        var server = new HealthPipeServer(source, new HealthcheckOptions
        {
            PipeEnabled = true,
            PipeName = pipeName,
            PipeMaxClients = maximumClients
        }, clock, CreateActualEndpoint);
        Exception? primary = null;
        try
        {
            Task starting = server.StartAsync();
            if (OperatingSystem.IsWindows()) { Assert.ThrowsExactly<System.ComponentModel.Win32Exception>(() => Observe(starting)); }
            else { Assert.ThrowsExactly<UnauthorizedAccessException>(() => Observe(starting)); }
            Assert.AreEqual(2, attempts, "The first real endpoint must exist before the second actual creation fails.");
            Assert.IsNotNull(firstEndpoint);
            if (OperatingSystem.IsWindows())
            {
                Assert.IsNotNull(firstHandle);
                Assert.IsTrue(firstHandle.IsClosed, "The failed partial startup must close its actual first endpoint before returning its failure.");
            }
            Assert.Throws<InvalidOperationException>(() => server.StartAsync());
        }
        catch (Exception error) { primary = error; }
        return FinishPhysicalStartupObservation(server, source, clock, pipeName, maximumClients, firstEndpoint, firstHandle, primary);

        NamedPipeServerStream CreateActualEndpoint(string name, int maximumClients, bool first)
        {
            int attempt = Interlocked.Increment(ref attempts);
            // The deliberate second first-instance request produces the same
            // genuine native collision exercised by the endpoint boundary test.
            // It models an actual factory I/O failure after partial allocation;
            // no exception or endpoint is fabricated by the fixture.
            NamedPipeServerStream endpoint = HealthPipeEndpoint.Create(name, maximumClients, first: true);
            if (attempt == 1)
            {
                firstEndpoint = endpoint;
                if (OperatingSystem.IsWindows())
                {
                    firstHandle = endpoint.SafePipeHandle;
                    Assert.IsFalse(firstHandle.IsClosed);
                }
                return endpoint;
            }
            endpoint.Dispose();
            Assert.Fail("A second protected first-instance creation must collide with the actual first endpoint.");
            throw new InvalidOperationException("The actual creation collision was not enforced.");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static StartupObservation ObserveUnpublishedInitialHandle()
    {
        var source = new UnrequestedSource();
        var clock = new NoRequestClock();
        using var created = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        NamedPipeServerStream? endpoint = null;
        SafePipeHandle? handle = null;
        string pipeName = "hc-unpublished-start-" + Guid.NewGuid().ToString("N");
        const int maximumClients = 1;
        var server = new HealthPipeServer(source, new HealthcheckOptions
        {
            PipeEnabled = true,
            PipeName = pipeName,
            PipeMaxClients = maximumClients
        }, clock, CreateActualHeldEndpoint);
        var returned = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        var starter = new Thread(() =>
        {
            try { returned.SetResult(server.StartAsync()); }
            catch (Exception error) { returned.SetException(error); }
        })
        { IsBackground = true, Name = "Healthcheck unpublished initial endpoint" };
        Exception? primary = null;
        Task? stopping = null;
        bool started = false;
        try
        {
            starter.Start(); started = true;
            Assert.IsTrue(created.Wait(Deadline), "The real endpoint must be held before publication.");
            Assert.IsNotNull(endpoint);
            if (OperatingSystem.IsWindows())
            {
                Assert.IsNotNull(handle);
                Assert.IsFalse(handle.IsClosed);
            }
            stopping = StartStop(server);
            Assert.IsFalse(stopping.IsCompleted, "Stop must retain the actual in-progress creation owner.");
        }
        catch (Exception error) { primary = error; }
        finally
        {
            release.Set();
            if (started && !starter.Join(Deadline))
            {
                primary = AddPhysicalStartupFailure(primary, new TimeoutException("The actual initial creation thread did not return."));
            }
            if (returned.Task.IsCompletedSuccessfully)
            {
                try { Observe(returned.Task.Result); }
                catch (Exception error) { primary = AddPhysicalStartupFailure(primary, error); }
            }
            else if (returned.Task.Exception is { } error) { primary = AddPhysicalStartupFailure(primary, error); }
        }
        return FinishPhysicalStartupObservation(server, source, clock, pipeName, maximumClients, endpoint, handle, primary, stopping);

        NamedPipeServerStream CreateActualHeldEndpoint(string name, int maximumClients, bool first)
        {
            endpoint = HealthPipeEndpoint.Create(name, maximumClients, first);
            if (OperatingSystem.IsWindows()) { handle = endpoint.SafePipeHandle; }
            created.Set();
            if (!release.Wait(Deadline)) { endpoint.Dispose(); throw new TimeoutException("Actual initial creation was not released."); }
            return endpoint;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static StartupObservation FinishPhysicalStartupObservation(HealthPipeServer server, UnrequestedSource source, NoRequestClock clock,
        string pipeName, int maximumClients, NamedPipeServerStream? observedEndpoint, SafePipeHandle? observedHandle,
        Exception? primary, Task? stopping = null)
    {
        stopping ??= StartStop(server);
        Task disposing = StartDispose(server);
        var cleanup = new CleanupObservation();
        WeakReference<Task>[] observers = [ObserveWhenCompleted(stopping, cleanup, isDispose: false),
            ObserveWhenCompleted(disposing, cleanup, isDispose: true)];
        Task terminals = Task.WhenAll(stopping, disposing);
        try
        {
            // This guard is specific to a returned factory/start call with no
            // request and closed real endpoints. It is not an inline-completion
            // promise for general Stop/Dispose. One combined guard stays below
            // the native mutation runner's additional ten-second allowance.
            terminals.WaitAsync(UnrequestedTerminalDeadline).GetAwaiter().GetResult();
        }
        catch (TimeoutException error)
        {
            primary = AddPhysicalStartupFailure(primary, new AssertFailedException(
                "After actual no-request startup work returns, Stop and Dispose must settle within this bounded control.", error));
        }
        catch (Exception error) { primary = AddPhysicalStartupFailure(primary, terminals.Exception ?? error); }
        try
        {
            Assert.IsNotNull(observedEndpoint);
            if (OperatingSystem.IsWindows())
            {
                Assert.IsNotNull(observedHandle);
                Assert.IsTrue(observedHandle.IsClosed,
                    "The actual unpublished endpoint must be closed before terminal completion; later collection is not ownership release.");
            }
            else
            {
                // Unix has no accepted SafePipeHandle yet. A protected first
                // instance can be rebound only after the real listener owner
                // releases this name. Keep its original stream strongly alive
                // until after this OS ownership assertion, before rescue or GC.
                using NamedPipeServerStream rebound = HealthPipeEndpoint.Create(pipeName, maximumClients, first: true);
            }
            Assert.AreEqual(0, source.Captures);
            Assert.AreEqual(0, clock.TimerCreations);
        }
        catch (Exception error) { primary = AddPhysicalStartupFailure(primary, error); }
        finally
        {
            // This strong observer is used after the product assertion, so GC
            // cannot close a missing-disposal mutant before it is observed.
            // Fixture rescue never erases that saved ownership failure.
            try { observedEndpoint?.Dispose(); }
            catch (Exception error) { primary = AddPhysicalStartupFailure(primary, error); }
        }
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), new WeakReference(server), cleanup, observers,
            primary is null, source, clock);
    }

    private static Exception AddPhysicalStartupFailure(Exception? primary, Exception failure)
        => primary is null ? failure : new AggregateException("The actual startup ownership control and cleanup failed.", primary, failure);

    private static void ReviewStartupObservation(StartupObservation observed)
    {
        if (observed.QueuedOrAcceptingOwner is { } owner)
        {
            CollectUntilDead(owner);
            if (owner.IsAlive)
            {
                var hold = new InvalidOperationException("Queued callbacks and pending asynchronous accept owners are not excused by an incomplete or prematurely completed managed stop signal.");
                if (observed.Primary is null) { throw hold; }
                throw new AggregateException("The enabled-startup assertion failed and real pipe-server ownership remained reachable.",
                    observed.Primary.SourceException, hold);
            }
            try
            {
                if (Volatile.Read(ref observed.Source.Captures) != 0 || Volatile.Read(ref observed.Clock.TimerCreations) != 0)
                {
                    throw new InvalidOperationException("Actual source/request-budget work appeared during owner observation; its cleanup remains a hold.");
                }
                // An incomplete unreachable managed-signal cycle may have been
                // collected. A surviving observer can instead be queued or
                // running after real completion; join it before checking the
                // independent fault store. No observer strongly returned from
                // the helper is allowed to keep the server alive.
                foreach (WeakReference<Task> reference in observed.CleanupObservers)
                {
                    if (reference.TryGetTarget(out Task? actualObserver)) { Observe(actualObserver); }
                }
                Exception[] failures = observed.Cleanup!.Failures();
                if (failures.Length > 0) { throw new AggregateException("Actual Stop/Dispose failed.", failures); }
            }
            catch (Exception error)
            {
                if (observed.Primary is null) { throw; }
                throw new AggregateException("The enabled-startup assertion and actual cleanup both failed.",
                    observed.Primary.SourceException, error);
            }
            if (observed.RequireSuccessfulTerminalCompletion)
            {
                // These are actual terminal Task observations, after the real
                // no-request owner has gone and every surviving observer was
                // joined. A collected unresolved managed notification does not
                // count as successful API completion. No deadline gives credit.
                Assert.IsTrue(observed.Cleanup!.StopCompletedSuccessfully,
                    "Actual Stop must complete successfully after its real queued/accepting owner has been released.");
                Assert.IsTrue(observed.Cleanup.DisposeCompletedSuccessfully,
                    "Actual Dispose must complete successfully after its real queued/accepting owner has been released.");
            }
        }
        observed.Primary?.Throw();
    }

    // No returned Task or helper async state machine may retain the actual
    // server. A queued RunSlot callback or pending RunSlotAsync accept must
    // still root it. The caller observes collection only after this returns.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static StartupObservation StartStopAndRelease(bool stopDuringConstruction = false)
    {
        var source = new UnrequestedSource();
        var clock = new NoRequestClock();
        var options = new HealthcheckOptions
        {
            PipeEnabled = true,
            PipeName = "hc-startup-owner-" + Guid.NewGuid().ToString("N"),
            PipeMaxClients = 1,
        };
        using var created = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var server = stopDuringConstruction
            ? new HealthPipeServer(source, options, clock, CreateActualHeldEndpoint)
            : new HealthPipeServer(source, options, clock);
        Exception? primary = null;
        bool observedStartupFailure = false;
        WeakReference? queuedOrAcceptingOwner = null;
        bool requireSuccessfulTerminalCompletion = false;
        CleanupObservation? cleanupObservation = null;
        WeakReference<Task>[] cleanupObservers = [];
        try
        {
            Exception? startupFailure = null;
            string? originalOwnedQueueFailure = null;
            Task? starting = null;
            try { starting = stopDuringConstruction ? StartWhileHoldingActualConstruction(server, created, release) : server.StartAsync(); }
            catch (NullReferenceException error)
            {
                // A genuinely accepted worker can clear its own slot while the
                // failed-start finally still dereferences that unrecorded slot.
                // The saved start task was faulted before that synchronous
                // finally failure prevented the public call from returning it.
                // Observe that exact owned fault, rather than abandon it or
                // repeat Start and risk scheduling another worker.
                originalOwnedQueueFailure = ObserveOriginalOwnedQueueFault(server).ToString();
                startupFailure = error;
                observedStartupFailure = true;
            }
            if (starting is not null)
            {
                try { Observe(starting); }
                catch (InvalidOperationException error)
                {
                    startupFailure = error;
                    observedStartupFailure = true;
                }
            }
            // This observes the actual returned task; it imposes no inline
            // completion, callback scheduling or endpoint-publication SLA.
            Assert.IsNull(startupFailure,
                "Enabled startup with a fresh local endpoint and one accepted worker must succeed: " + startupFailure
                + (originalOwnedQueueFailure is null ? "" : "\nOriginal actual owned startup fault (observed without changing it):\n" + originalOwnedQueueFailure));
            Assert.AreEqual(0, source.Captures, "No client or request was submitted.");
            Assert.AreEqual(0, clock.TimerCreations, "An accepting slot has no request budget to create.");
        }
        catch (Exception error) { primary = error; }
        finally
        {
            Task stopping = StartStop(server);
            Task disposing = StartDispose(server);
            Task? drains = null;
            try
            {
                bool successfulNoRequestStart = primary is null;
                bool candidateAccountingSignal = (successfulNoRequestStart || observedStartupFailure && primary is AssertFailedException)
                    && Volatile.Read(ref source.Captures) == 0 && Volatile.Read(ref clock.TimerCreations) == 0
                    && !stopping.IsFaulted && !stopping.IsCanceled
                    && !disposing.IsFaulted && !disposing.IsCanceled;
                if (candidateAccountingSignal)
                {
                    // Actual successful Start is already joined above. No client
                    // was constructed. For the reviewed one-slot source, terminal
                    // BeginStop closes every published non-null stream; a null
                    // stream belongs to RunSlot's actual departing finally.
                    // Real queued/accepting work still roots the server until
                    // it returns. This is not a claim that an unstarted or
                    // omitted callback executed an accepting worker.
                    cleanupObservation = new();
                    cleanupObservers = [ObserveWhenCompleted(stopping, cleanupObservation, isDispose: false),
                        ObserveWhenCompleted(disposing, cleanupObservation, isDispose: true)];
                    queuedOrAcceptingOwner = new WeakReference(server);
                    // Known failed-start paths preserve their existing startup
                    // assertion without claiming terminal API success. Only an
                    // actually successful Start adds the completion obligation.
                    requireSuccessfulTerminalCompletion = successfulNoRequestStart;
                }
                else { drains = Task.WhenAll(stopping, disposing); Observe(drains); }
            }
            catch (Exception error)
            {
                primary = primary is null ? error
                    : new AggregateException("The startup control and actual pipe cleanup both failed.",
                        primary, drains?.Exception ?? error);
            }
        }
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), queuedOrAcceptingOwner,
            cleanupObservation, cleanupObservers, requireSuccessfulTerminalCompletion, source, clock);

        NamedPipeServerStream CreateActualHeldEndpoint(string name, int maximumClients, bool first)
        {
            NamedPipeServerStream endpoint = HealthPipeEndpoint.Create(name, maximumClients, first);
            try
            {
                created.Set();
                if (!release.Wait(Deadline)) { throw new TimeoutException("The actual endpoint construction was not released."); }
                return endpoint;
            }
            catch { endpoint.Dispose(); throw; }
        }
    }

    private static Task StartWhileHoldingActualConstruction(HealthPipeServer server, ManualResetEventSlim created,
        ManualResetEventSlim release)
    {
        var returned = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        var starter = new Thread(() =>
        {
            try { returned.SetResult(server.StartAsync()); }
            catch (Exception error) { returned.SetException(error); }
        })
        { IsBackground = true, Name = "Healthcheck actual startup boundary" };
        starter.Start();
        try
        {
            Assert.IsTrue(created.Wait(Deadline), "Startup must actually create its owned OS endpoint before this observation.");
            Task stopping = server.StopAsync();
            Assert.IsFalse(stopping.IsCompleted, "Terminal completion must await the actual in-progress endpoint construction.");
            Assert.Throws<InvalidOperationException>(() => server.StartAsync());
        }
        finally
        {
            release.Set();
            if (!starter.Join(Deadline)) { throw new TimeoutException("The actual startup thread did not leave endpoint construction."); }
        }
        return returned.Task.GetAwaiter().GetResult();
    }

    private static void Observe(Task actual) => actual.WaitAsync(Deadline).GetAwaiter().GetResult();

    private static InvalidOperationException ObserveOriginalOwnedQueueFault(HealthPipeServer server)
    {
        // Cleanup-only coupling to the actual existing owner. No field writes,
        // worker invocation, replacement task or completion operation occurs.
        // The behavioral assertion above never reads private startup state.
        FieldInfo? field = typeof(HealthPipeServer).GetField("startCompletion", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.FieldType != typeof(TaskCompletionSource) || field.GetValue(server) is not TaskCompletionSource owner)
        {
            throw new InvalidOperationException("Cleanup could not identify the original owned startup completion task after synchronous startup failure.");
        }
        Task actual = owner.Task;
        if (!actual.IsFaulted || actual.Exception is not { } aggregate)
        {
            throw new InvalidOperationException("The original owned startup task did not contain the expected completed failure; its actual work cannot be excused.");
        }
        // Reading Exception observes every original inner failure. A different
        // error is preserved as an actual cleanup hold, never assertion credit.
        Exception[] failures = aggregate.Flatten().InnerExceptions.ToArray();
        if (failures.Length != 1 || failures[0].GetType() != typeof(InvalidOperationException)
            || failures[0].Message != "The Healthcheck pipe worker could not be queued."
            || failures[0].TargetSite?.Name != "StartCore"
            || failures[0].TargetSite?.DeclaringType != typeof(HealthPipeServer))
        {
            throw new AggregateException("Cleanup observed an unexpected original owned startup fault.", failures);
        }
        return (InvalidOperationException)failures[0];
    }

    private static Task StartStop(HealthPipeServer server)
    {
        try { return server.StopAsync(); }
        catch (Exception error) { return Task.FromException(error); }
    }

    private static Task StartDispose(HealthPipeServer server)
    {
        try { return server.DisposeAsync().AsTask(); }
        catch (Exception error) { return Task.FromException(error); }
    }

    private static void CollectUntilDead(WeakReference reference)
    {
        var observation = Stopwatch.StartNew();
        while (reference.IsAlive && observation.Elapsed < Deadline)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            // Give the actual accepted callback/continuation CPU to return.
            // Elapsed time cannot certify settlement: a live owner at expiry
            // remains the explicit cleanup failure checked by the caller.
            if (reference.IsAlive && observation.Elapsed < Deadline) { Thread.Sleep(1); }
        }
    }

    private static WeakReference<Task> ObserveWhenCompleted(Task actual, CleanupObservation observation, bool isDispose)
    {
        Task observer = actual.ContinueWith(static (completed, state) =>
        {
            var observed = ((CleanupObservation Observation, bool IsDispose))state!;
            if (completed.Exception is { } failure) { observed.Observation.Add(failure); }
            else if (completed.IsCanceled) { observed.Observation.Add(new TaskCanceledException("Actual pipe cleanup was canceled.")); }
            else { observed.Observation.RecordSuccess(observed.IsDispose); }
        }, (observation, isDispose), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new(observer);
    }

    private sealed record StartupObservation(ExceptionDispatchInfo? Primary, WeakReference? QueuedOrAcceptingOwner,
        CleanupObservation? Cleanup, WeakReference<Task>[] CleanupObservers, bool RequireSuccessfulTerminalCompletion,
        UnrequestedSource Source, NoRequestClock Clock);

    private sealed class CleanupObservation
    {
        private readonly object gate = new();
        private readonly List<Exception> failures = [];
        private bool stopCompletedSuccessfully, disposeCompletedSuccessfully;
        internal bool StopCompletedSuccessfully { get { lock (gate) { return stopCompletedSuccessfully; } } }
        internal bool DisposeCompletedSuccessfully { get { lock (gate) { return disposeCompletedSuccessfully; } } }
        internal void RecordSuccess(bool isDispose)
        {
            lock (gate)
            {
                if (isDispose) { disposeCompletedSuccessfully = true; }
                else { stopCompletedSuccessfully = true; }
            }
        }
        internal void Add(Exception error) { lock (gate) { failures.Add(error); } }
        internal Exception[] Failures() { lock (gate) { return [.. failures]; } }
    }

    private sealed class UnrequestedSource : IHealthSnapshotSource
    {
        internal int Captures;
        public HealthSnapshot CaptureSnapshot()
        {
            Interlocked.Increment(ref Captures);
            throw new InvalidOperationException("No client or snapshot request belongs to this startup-only scenario.");
        }
    }

    private sealed class NoRequestClock : TimeProvider
    {
        internal int TimerCreations;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref TimerCreations);
            throw new InvalidOperationException("No request budget belongs to this startup-only scenario.");
        }
    }
}

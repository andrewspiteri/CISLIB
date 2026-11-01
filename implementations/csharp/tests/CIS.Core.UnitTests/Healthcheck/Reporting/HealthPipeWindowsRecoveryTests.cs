using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using CIS.Healthcheck;
using CIS.Healthcheck.Pipes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32.SafeHandles;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
[TestCategory("ComponentIntegration")]
[DoNotParallelize]
public sealed class HealthPipeWindowsRecoveryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    public void APeerClosingBeforeActualAcceptRecoversTheSlotAndReleasesItsCreationGate()
        => ReviewRecovery(failReplacement: false);

    [TestMethod]
    public void ActualReplacementCreationFailureClosesPublicAdmissionAfterTheWorkerLeaves()
        => ReviewRecovery(failReplacement: true);

    [TestMethod]
    public void StopDuringActualReplacementCreationClosesItsUnpublishedHandleBeforeCollection()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("Windows pre-accept disconnect recovery is inapplicable on this platform.");
            return;
        }
        ReviewRecoveryObservation(ExerciseUnpublishedReplacement());
    }

    [TestMethod]
    public void ADisconnectedSlotRecoversWhileItsSiblingStillOwnsTheEndpointName()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("Windows pre-accept multi-slot recovery is inapplicable on this platform.");
            return;
        }
        ReviewRecoveryObservation(ExerciseRecoveryWithLiveSibling());
    }

    private static void ReviewRecovery(bool failReplacement)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("Windows ERROR_NO_DATA endpoint recovery is inapplicable on this platform.");
            return;
        }
        ReviewRecoveryObservation(ExerciseRecovery(failReplacement));
    }

    private static void ReviewRecoveryObservation(RecoveryObservation observed)
    {
        if (observed.Owner is { } owner)
        {
            var elapsed = Stopwatch.StartNew();
            while (owner.IsAlive && elapsed.Elapsed < Deadline)
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                if (owner.IsAlive) { Thread.Sleep(1); }
            }
            if (owner.IsAlive)
            {
                throw Combine(observed.Primary?.SourceException,
                    new InvalidOperationException("Actual recovery or accepting work still owns the server after terminal cleanup."));
            }
            // A collected unresolved notification is not successful Stop or
            // Dispose. Observe every surviving actual continuation and preserve
            // the behavioral failure; collection supplies no mutant kill credit.
            foreach (var reference in observed.Observers)
            {
                if (reference.TryGetTarget(out Task? observer)) { Observe(observer); }
            }
            if (observed.Cleanup.Failures() is { Length: > 0 } failures)
            {
                throw Combine(observed.Primary?.SourceException, new AggregateException("Actual recovery cleanup failed.", failures));
            }
            if (observed.Primary is null)
            {
                Assert.IsTrue(observed.Cleanup.StopSucceeded && observed.Cleanup.DisposeSucceeded,
                    "Actual successful recovery must retain successful terminal API completion.");
            }
        }
        Assert.AreEqual(observed.ExpectedCaptures, observed.Source.Captures, "No late source capture may escape the actual owned request.");
        observed.Primary?.Throw();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RecoveryObservation ExerciseUnpublishedReplacement()
    {
        var service = new HealthcheckService(new(), new HealthApplicationIdentity { Name = "unpublished replacement", InstanceId = Guid.NewGuid() });
        service.MarkStartupCompleted();
        HealthSnapshot before = service.CaptureSnapshot();
        var source = new CountingSource(service);
        using var firstCreated = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        using var replacementCreated = new ManualResetEventSlim();
        using var releaseReplacement = new ManualResetEventSlim();
        string name = "hc-unpublished-recovery-" + Guid.NewGuid().ToString("N");
        NamedPipeServerStream? firstEndpoint = null, replacement = null;
        SafePipeHandle? replacementHandle = null;
        int creations = 0;
        var server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = name, PipeMaxClients = 1 }, null, CreateActualEndpoint);
        var returned = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        var starter = new Thread(() =>
        {
            try { returned.SetResult(server.StartAsync()); }
            catch (Exception error) { returned.SetException(error); }
        })
        { IsBackground = true, Name = "Healthcheck unpublished replacement endpoint" };
        Exception? primary = null;
        Task? stopping = null;
        bool started = false;
        var cleanup = new CleanupObservation();
        WeakReference<Task>[] observers = [];
        try
        {
            starter.Start(); started = true;
            Assert.IsTrue(firstCreated.Wait(Deadline), "The real initial endpoint must exist before any accept worker can run.");
            using (var earlyPeer = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
            using (var connectionDeadline = new CancellationTokenSource(Deadline))
            {
                Observe(earlyPeer.ConnectAsync(connectionDeadline.Token));
                Assert.IsTrue(earlyPeer.IsConnected);
            }
            releaseFirst.Set();
            Assert.IsTrue(starter.Join(Deadline), "Actual initial startup must leave its factory.");
            Observe(returned.Task.WaitAsync(Deadline).GetAwaiter().GetResult());
            Assert.IsTrue(replacementCreated.Wait(Deadline), "The real Windows disconnect must reach actual replacement creation.");
            Assert.IsNotNull(replacementHandle);
            Assert.IsFalse(replacementHandle.IsClosed);
            stopping = Terminal(() => server.StopAsync());
            Assert.IsFalse(stopping.IsCompleted, "Stop must await the real replacement factory that is still held.");
        }
        catch (Exception error) { primary = error; }
        finally
        {
            releaseFirst.Set(); releaseReplacement.Set();
            if (started && !starter.Join(Deadline))
            {
                primary = Combine(primary, new TimeoutException("The actual initial startup thread did not return."));
            }
            if (returned.Task.IsCompletedSuccessfully)
            {
                try { Observe(returned.Task.Result); }
                catch (Exception error) { primary = Combine(primary, error); }
            }
            else if (returned.Task.Exception is { } startupFailure) { primary = Combine(primary, startupFailure); }
            stopping ??= Terminal(() => server.StopAsync());
            Task disposing = Terminal(() => server.DisposeAsync().AsTask());
            observers = [ObserveCompletion(stopping, cleanup, isDispose: false), ObserveCompletion(disposing, cleanup, isDispose: true)];
            Task terminals = Task.WhenAll(stopping, disposing);
            try { terminals.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult(); }
            catch (TimeoutException error)
            {
                primary = Combine(primary, new AssertFailedException("After releasing its real replacement factory, the no-request server must settle terminal ownership within this bounded control.", error));
            }
            catch (Exception error) { primary = Combine(primary, terminals.Exception ?? error); }
            try
            {
                Assert.IsNotNull(replacement);
                Assert.IsNotNull(replacementHandle);
                Assert.IsTrue(replacementHandle.IsClosed,
                    "Stop must close the actual unpublished replacement before terminal completion; GC is not endpoint disposal.");
                Assert.AreEqual(2, Volatile.Read(ref creations));
                Assert.AreEqual(0, source.Captures);
                HealthSnapshot after = service.CaptureSnapshot();
                Assert.AreEqual(before.Revision, after.Revision);
                Assert.AreEqual(before.HealthStatus, after.HealthStatus);
                Assert.AreEqual(before.Ready, after.Ready);
            }
            catch (Exception error) { primary = Combine(primary, error); }
            // Retain the actual streams through the physical ownership assertion.
            // Test-owned rescue occurs afterward and cannot erase its saved failure.
            try { replacement?.Dispose(); firstEndpoint?.Dispose(); }
            catch (Exception error) { primary = Combine(primary, error); }
            try { Observe(service.DisposeAsync().AsTask()); }
            catch (Exception error) { primary = Combine(primary, error); }
        }
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), new WeakReference(server), observers, cleanup, source, 0);

        NamedPipeServerStream CreateActualEndpoint(string pipe, int maximumClients, bool first)
        {
            int attempt = Interlocked.Increment(ref creations);
            NamedPipeServerStream endpoint = HealthPipeEndpoint.Create(pipe, maximumClients, first);
            if (attempt == 1)
            {
                firstEndpoint = endpoint;
                firstCreated.Set();
                if (!releaseFirst.Wait(Deadline)) { endpoint.Dispose(); throw new TimeoutException("The actual first endpoint was not released."); }
            }
            else if (attempt == 2)
            {
                replacement = endpoint;
                replacementHandle = endpoint.SafePipeHandle;
                replacementCreated.Set();
                if (!releaseReplacement.Wait(Deadline)) { endpoint.Dispose(); throw new TimeoutException("The actual replacement endpoint was not released."); }
            }
            return endpoint;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RecoveryObservation ExerciseRecoveryWithLiveSibling()
    {
        var service = new HealthcheckService(new(), new HealthApplicationIdentity { Name = "two-slot recovery", InstanceId = Guid.NewGuid() });
        service.MarkStartupCompleted();
        HealthSnapshot before = service.CaptureSnapshot();
        var source = new CountingSource(service);
        string name = "hc-sibling-recovery-" + Guid.NewGuid().ToString("N");
        using var firstCreated = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        var replacementCompleted = new TaskCompletionSource<NamedPipeServerStream>(TaskCreationOptions.RunContinuationsAsynchronously);
        NamedPipeServerStream? firstEndpoint = null, sibling = null, replacement = null;
        SafePipeHandle? siblingHandle = null, replacementHandle = null;
        bool siblingWasOpenForRecovery = false;
        int creations = 0;
        var server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = name, PipeMaxClients = 2 }, null, CreateActualEndpoint);
        var returned = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        var starter = new Thread(() =>
        {
            try { returned.SetResult(server.StartAsync()); }
            catch (Exception error) { returned.SetException(error); }
        })
        { IsBackground = true, Name = "Healthcheck actual two-slot recovery" };
        Exception? primary = null;
        bool started = false;
        var cleanup = new CleanupObservation();
        WeakReference<Task>[] observers = [];
        try
        {
            starter.Start(); started = true;
            Assert.IsTrue(firstCreated.Wait(Deadline), "The first real endpoint must be held before either accept worker can run.");
            using (var earlyPeer = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
            using (var connectionDeadline = new CancellationTokenSource(Deadline))
            {
                Observe(earlyPeer.ConnectAsync(connectionDeadline.Token));
                Assert.IsTrue(earlyPeer.IsConnected);
            }
            releaseFirst.Set();
            Assert.IsTrue(starter.Join(Deadline), "Actual two-slot startup must leave its endpoint factory.");
            Observe(returned.Task.WaitAsync(Deadline).GetAwaiter().GetResult());
            // A real replacement collision faults this fixture-owned observation
            // immediately. It is not hidden behind a native runner timeout.
            replacementCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            Assert.IsTrue(siblingWasOpenForRecovery, "The other actual initial endpoint must still own this pipe name during recovery.");
            Assert.IsNotNull(siblingHandle);
            Assert.IsFalse(siblingHandle.IsClosed);
            Assert.IsNotNull(replacementHandle);
            Assert.IsFalse(replacementHandle.IsClosed);

            using var output = new MemoryStream();
            using var error = new StringWriter();
            using var requestDeadline = new CancellationTokenSource(Deadline);
            Task<int> probe = HealthProbeClient.RunAsync(["--pipe", name, "--timeout-ms", "5000"], output, error, requestDeadline.Token);
            try
            {
                Assert.AreEqual(0, probe.WaitAsync(Deadline).GetAwaiter().GetResult(), error.ToString());
                Assert.AreEqual("", error.ToString());
                using var response = JsonDocument.Parse(Encoding.UTF8.GetString(output.ToArray()));
                Assert.AreEqual("Healthy", response.RootElement.GetProperty("healthStatus").GetString());
            }
            finally
            {
                if (!probe.IsCompleted) { requestDeadline.Cancel(); }
                Observe(probe);
            }
            Assert.AreEqual(1, source.Captures, "Only the actual healthy probe may capture application health.");
            Assert.IsTrue(Volatile.Read(ref creations) >= 3, "Both real initial endpoints and actual replacement must have been created.");
            HealthSnapshot after = service.CaptureSnapshot();
            Assert.AreEqual(before.Revision, after.Revision);
            Assert.AreEqual(before.HealthStatus, after.HealthStatus);
            Assert.AreEqual(before.Ready, after.Ready);
        }
        catch (Exception error) { primary = error; }
        finally
        {
            releaseFirst.Set();
            if (started && !starter.Join(Deadline)) { primary = Combine(primary, new TimeoutException("Actual two-slot startup did not return.")); }
            if (returned.Task.IsCompletedSuccessfully)
            {
                try { Observe(returned.Task.Result); }
                catch (Exception error) { primary = Combine(primary, error); }
            }
            else if (returned.Task.Exception is { } startupFailure) { primary = Combine(primary, startupFailure); }
            Task stopping = Terminal(() => server.StopAsync());
            Task disposing = Terminal(() => server.DisposeAsync().AsTask());
            observers = [ObserveCompletion(stopping, cleanup, isDispose: false), ObserveCompletion(disposing, cleanup, isDispose: true)];
            Task terminals = Task.WhenAll(stopping, disposing);
            try { terminals.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult(); }
            catch (TimeoutException error) { primary = Combine(primary, new AssertFailedException("After actual two-slot recovery work returns, terminal ownership must settle within this bounded control.", error)); }
            catch (Exception error) { primary = Combine(primary, terminals.Exception ?? error); }
            try
            {
                if (siblingHandle is not null) { Assert.IsTrue(siblingHandle.IsClosed, "Stop must close its actual sibling endpoint before fixture rescue or GC."); }
                if (replacementHandle is not null) { Assert.IsTrue(replacementHandle.IsClosed, "Stop must close its actual replacement before fixture rescue or GC."); }
                if (primary is null) { Assert.AreEqual(1, source.Captures, "Terminal cleanup must not submit an additional application health capture."); }
            }
            catch (Exception error) { primary = Combine(primary, error); }
            // The original actual streams remain strong observers through the
            // physical assertions. Rescue cannot erase an already saved failure.
            try { replacement?.Dispose(); sibling?.Dispose(); firstEndpoint?.Dispose(); }
            catch (Exception error) { primary = Combine(primary, error); }
            try { Observe(service.DisposeAsync().AsTask()); }
            catch (Exception error) { primary = Combine(primary, error); }
        }
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), new WeakReference(server), observers, cleanup, source, primary is null ? 1 : source.Captures);

        NamedPipeServerStream CreateActualEndpoint(string pipe, int maximumClients, bool first)
        {
            int attempt = Interlocked.Increment(ref creations);
            if (attempt == 3) { siblingWasOpenForRecovery = siblingHandle is { IsClosed: false }; }
            NamedPipeServerStream endpoint;
            try { endpoint = HealthPipeEndpoint.Create(pipe, maximumClients, first); }
            catch (Exception error)
            {
                if (attempt == 3) { replacementCompleted.TrySetException(error); }
                throw;
            }
            if (attempt == 1)
            {
                firstEndpoint = endpoint;
                firstCreated.Set();
                if (!releaseFirst.Wait(Deadline)) { endpoint.Dispose(); throw new TimeoutException("The actual first endpoint was not released."); }
            }
            else if (attempt == 2) { sibling = endpoint; siblingHandle = endpoint.SafePipeHandle; }
            else if (attempt == 3)
            {
                replacement = endpoint; replacementHandle = endpoint.SafePipeHandle;
                replacementCompleted.TrySetResult(endpoint);
            }
            return endpoint;
        }
    }

    // No returned asynchronous state machine keeps the server alive during
    // cleanup observation. The endpoint, startup thread and every real request
    // are owned here; no worker or private state is manufactured or changed.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RecoveryObservation ExerciseRecovery(bool failReplacement)
    {
        var service = new HealthcheckService(new(), new HealthApplicationIdentity { Name = "Windows pipe recovery", InstanceId = Guid.NewGuid() });
        service.MarkStartupCompleted();
        var source = new CountingSource(service);
        string name = "hc-recovery-" + Guid.NewGuid().ToString("N");
        using var created = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var replacementAttempted = new ManualResetEventSlim();
        int endpointCreations = 0;
        var server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = name, PipeMaxClients = 1 }, null, CreateActualEndpoint);
        var returned = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        var starter = new Thread(() =>
        {
            try { returned.SetResult(server.StartAsync()); }
            catch (Exception error) { returned.SetException(error); }
        })
        { IsBackground = true, Name = "Healthcheck actual pre-accept recovery boundary" };
        Exception? primary = null;
        WeakReference? owner = null;
        WeakReference<Task>[] observers = [];
        var cleanup = new CleanupObservation();
        bool starterStarted = false;
        try
        {
            HealthSnapshot before = service.CaptureSnapshot();
            Assert.AreEqual(HealthStatus.Healthy, before.HealthStatus);
            Assert.IsTrue(before.Ready);
            starter.Start();
            starterStarted = true;
            Assert.IsTrue(created.Wait(Deadline), "The actual first endpoint must exist while its factory is held before publication.");
            using (var earlyPeer = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
            using (var connectionDeadline = new CancellationTokenSource(Deadline))
            {
                Observe(earlyPeer.ConnectAsync(connectionDeadline.Token));
                Assert.IsTrue(earlyPeer.IsConnected);
            }
            // The connected client has actually closed while no slot worker
            // can yet have called WaitForConnectionAsync on this endpoint.
            release.Set();
            Assert.IsTrue(starter.Join(Deadline), "Actual startup must return after its endpoint factory is released.");
            Task actualStart = returned.Task.WaitAsync(Deadline).GetAwaiter().GetResult();
            Observe(actualStart);
            Assert.IsTrue(replacementAttempted.Wait(Deadline), "The real Windows pre-accept disconnect must enter endpoint recovery.");
            if (failReplacement)
            {
                WaitUntilActualWorkerLeaves(server);
                // Assert terminal public admission before calling Stop. A lost
                // worker cannot silently leave a publicly running empty server.
                Assert.Throws<InvalidOperationException>(() => server.StartAsync());
                Assert.AreEqual(0, source.Captures, "Recovery never submits a business snapshot request.");
            }
            else
            {
                using var output = new MemoryStream();
                using var error = new StringWriter();
                using var requestDeadline = new CancellationTokenSource(Deadline);
                Task<int> probe = HealthProbeClient.RunAsync(["--pipe", name, "--timeout-ms", "5000"], output, error, requestDeadline.Token);
                try
                {
                    Assert.AreEqual(0, probe.WaitAsync(Deadline).GetAwaiter().GetResult(), error.ToString());
                    Assert.AreEqual("", error.ToString());
                    using var response = JsonDocument.Parse(Encoding.UTF8.GetString(output.ToArray()));
                    Assert.AreEqual("Healthy", response.RootElement.GetProperty("healthStatus").GetString());
                }
                finally
                {
                    if (!probe.IsCompleted) { requestDeadline.Cancel(); }
                    Observe(probe);
                }
                Assert.AreEqual(1, source.Captures, "Only the real healthy probe may capture application health.");
                SemaphoreSlim changes = ReadOwnedField<SemaphoreSlim>(server, "endpointChanges");
                Assert.IsTrue(changes.AvailableWaitHandle.WaitOne(Deadline),
                    "Successful actual replacement must release the owned endpoint-change gate for future sequential capacity.");
            }
            int creations = Volatile.Read(ref endpointCreations);
            Assert.IsTrue(failReplacement ? creations == 2 : creations >= 2,
                "The original real endpoint and its recovery attempt must both be owned; a failed replacement must leave no new accepting worker.");
            HealthSnapshot after = service.CaptureSnapshot();
            Assert.AreEqual(before.Revision, after.Revision);
            Assert.AreEqual(before.HealthStatus, after.HealthStatus);
            Assert.AreEqual(before.Ready, after.Ready);
            Assert.AreEqual(before.Application.StartupCompleted, after.Application.StartupCompleted);
            Assert.AreEqual(before.Application.IsStopping, after.Application.IsStopping);
            Assert.AreEqual(before.MonitorCounts.Active, after.MonitorCounts.Active);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                observation = "actual Windows pre-accept disconnect recovery",
                processId = Environment.ProcessId,
                failedReplacement = failReplacement,
                actualEndpointCreations = creations,
                sourceCaptures = source.Captures,
                applicationHealthUnchanged = true
            }));
        }
        catch (Exception error) { primary = error; }
        finally
        {
            release.Set();
            if (starterStarted && !starter.Join(Deadline))
            {
                primary = Combine(primary, new TimeoutException("The actual endpoint-construction thread did not leave."));
            }
            if (returned.Task.IsCompletedSuccessfully)
            {
                try { Observe(returned.Task.Result); }
                catch (Exception error) { primary = Combine(primary, error); }
            }
            else if (returned.Task.Exception is { } startupFailure) { primary = Combine(primary, startupFailure); }
            Task stopping = Terminal(() => server.StopAsync());
            Task disposing = Terminal(() => server.DisposeAsync().AsTask());
            observers = [ObserveCompletion(stopping, cleanup, isDispose: false), ObserveCompletion(disposing, cleanup, isDispose: true)];
            try { Observe(Task.WhenAll(stopping, disposing)); }
            catch (TimeoutException) when (!stopping.IsCompleted || !disposing.IsCompleted) { owner = new WeakReference(server); }
            catch (Exception error) { primary = Combine(primary, error); }
            try { Observe(service.DisposeAsync().AsTask()); }
            catch (Exception error) { primary = Combine(primary, error); }
        }
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), owner, observers, cleanup, source, source.Captures);

        NamedPipeServerStream CreateActualEndpoint(string pipe, int maximumClients, bool first)
        {
            int attempt = Interlocked.Increment(ref endpointCreations);
            NamedPipeServerStream endpoint = HealthPipeEndpoint.Create(pipe, maximumClients, first);
            if (attempt == 1)
            {
                try
                {
                    created.Set();
                    if (!release.Wait(Deadline)) { throw new TimeoutException("The actual pre-publication endpoint was not released."); }
                    return endpoint;
                }
                catch { endpoint.Dispose(); throw; }
            }
            replacementAttempted.Set();
            if (!failReplacement) { return endpoint; }
            endpoint.Dispose();
            throw new IOException("Controlled failure after creating and disposing the real replacement endpoint.");
        }
    }

    private static void WaitUntilActualWorkerLeaves(HealthPipeServer server)
    {
        object gate = ReadOwnedField<object>(server, "gate");
        FieldInfo field = typeof(HealthPipeServer).GetField("remaining", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The actual worker drain counter was not found.");
        if (field.FieldType != typeof(int)) { throw new InvalidOperationException("The actual worker drain counter type changed."); }
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < Deadline)
        {
            lock (gate) { if ((int)field.GetValue(server)! == 0) { return; } }
            Thread.Sleep(1);
        }
        throw new TimeoutException("The actual recovery worker did not leave its owned drain accounting.");
    }

    private static T ReadOwnedField<T>(HealthPipeServer server, string name) where T : class
        => typeof(HealthPipeServer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(server) as T
            ?? throw new InvalidOperationException("The actual owned pipe-server field changed: " + name);

    private static Task Terminal(Func<Task> call) { try { return call(); } catch (Exception error) { return Task.FromException(error); } }
    private static void Observe(Task task) => task.WaitAsync(Deadline).GetAwaiter().GetResult();
    private static Exception Combine(Exception? primary, Exception secondary)
        => primary is null ? secondary : new AggregateException("The Windows recovery control and actual cleanup both failed.", primary, secondary);

    private static WeakReference<Task> ObserveCompletion(Task task, CleanupObservation observation, bool isDispose)
        => new(task.ContinueWith(static (completed, state) =>
        {
            var value = ((CleanupObservation Observation, bool IsDispose))state!;
            if (completed.Exception is { } error) { value.Observation.Add(error); }
            else if (completed.IsCanceled) { value.Observation.Add(new TaskCanceledException("Actual recovery cleanup was canceled.")); }
            else { value.Observation.Success(value.IsDispose); }
        }, (observation, isDispose), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default));

    private sealed record RecoveryObservation(ExceptionDispatchInfo? Primary, WeakReference? Owner, WeakReference<Task>[] Observers,
        CleanupObservation Cleanup, CountingSource Source, int ExpectedCaptures);

    private sealed class CleanupObservation
    {
        private readonly object gate = new();
        private readonly List<Exception> failures = [];
        private bool stopSucceeded, disposeSucceeded;
        internal bool StopSucceeded { get { lock (gate) { return stopSucceeded; } } }
        internal bool DisposeSucceeded { get { lock (gate) { return disposeSucceeded; } } }
        internal void Success(bool isDispose) { lock (gate) { if (isDispose) { disposeSucceeded = true; } else { stopSucceeded = true; } } }
        internal void Add(Exception error) { lock (gate) { failures.Add(error); } }
        internal Exception[] Failures() { lock (gate) { return [.. failures]; } }
    }

    private sealed class CountingSource(HealthcheckService service) : IHealthSnapshotSource
    {
        private int captures;
        internal int Captures => Volatile.Read(ref captures);
        public HealthSnapshot CaptureSnapshot() { Interlocked.Increment(ref captures); return service.CaptureSnapshot(); }
    }
}

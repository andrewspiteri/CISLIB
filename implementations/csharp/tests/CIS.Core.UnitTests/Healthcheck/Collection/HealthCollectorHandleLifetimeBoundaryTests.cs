using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

// Each case observes one actual public-call boundary with one forced collection.
[TestClass]
[DoNotParallelize]
public sealed class HealthCollectorHandleLifetimeBoundaryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(3);

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void IdentifyTheActualLoadedCoreBody()
    {
        var assembly = typeof(HealthCollectorRunner).Assembly;
        string hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location)));
        TestContext.WriteLine($"CIS_CORE_BODY {hash} {assembly.ManifestModule.ModuleVersionId:D}");
    }

    [TestMethod]
    public Task PeriodicActivationRetainsItsMonitorThroughTheActualClockCallback()
    {
        var clock = new CollectionClock();
        return RunAsync(scope =>
        {
            clock.Service = scope.Service;
            ActivateAsLastHandleUse(scope, clock);
            Assert.AreEqual(1, clock.Callbacks);
            Assert.AreEqual(1, clock.Collections);
            Assert.IsTrue(clock.MonitorPresent,
                "A registration being activated must retain its monitor through the actual clock callback.");
            Assert.IsTrue(clock.HandleAlive);
            return Task.CompletedTask;
        }, clock);
    }

    [TestMethod]
    public Task ManualCollectionRetainsItsMonitorWhileItsActualAdmissionIsBlocked()
        => RunGateBoundaryAsync(collect: true);

    [TestMethod]
    public Task WithdrawalRetainsItsMonitorUntilItsActualPublicCallReturns()
        => RunGateBoundaryAsync(collect: false);

    private static Task RunGateBoundaryAsync(bool collect) => RunAsync(async scope =>
    {
        scope.StartOwnedCall(collect);
        Assert.IsTrue(scope.Ready.Wait(Deadline), "The owned caller must finish its real registration setup.");
        if (scope.Returned.Task.IsCompleted) { await scope.Returned.Task; }

        FieldInfo? field = typeof(HealthCollectorRunner).GetField("gate", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field);
        object? gate = field.GetValue(scope.Runner);
        Assert.IsNotNull(gate);
        Assert.IsTrue(Monitor.TryEnter(gate, Deadline), "The fixture must own the runner's existing gate.");
        try
        {
            scope.Go.Set();
            Assert.IsTrue(scope.Entered.Wait(Deadline));
            bool blocked = SpinWait.SpinUntil(() => scope.Returned.Task.IsCompleted ||
                (scope.Thread!.ThreadState & ThreadState.WaitSleepJoin) != 0, Deadline);
            Assert.IsTrue(blocked && !scope.Returned.Task.IsCompleted,
                "After its setup wait, the owned public caller must block on the fixture-held real gate.");

            // One collection, no pending-finalizer wait and no retry. The caller
            // itself supplies the only strong handle reference at this boundary.
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            Assert.IsTrue(scope.Service.CaptureSnapshot().Monitors.Any(monitor => monitor.MonitorId == scope.MonitorId),
                "The active public registration call must keep its monitor contribution present.");
            Assert.IsTrue(scope.Handle!.IsAlive);
            Assert.IsTrue(scope.Monitor!.IsAlive);

            // Recover a caller-held handle only AFTER the measured boundary.
            // This prevents legitimate abandonment after return from affecting
            // the later actual-attempt and cleanup assertions.
            scope.RetainedAfterObservation = scope.Handle.Target as IHealthCollectionRegistration;
            Assert.IsNotNull(scope.RetainedAfterObservation);
        }
        finally { Monitor.Exit(gate); scope.Go.Set(); }

        await scope.JoinAsync();
        await scope.Returned.Task.WaitAsync(Deadline);
        if (collect)
        {
            Assert.IsNotNull(scope.Caller);
            HealthCollectionAttempt attempt = await scope.Caller.WaitAsync(Deadline);
            Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
            Assert.IsFalse(attempt.EvaluationFailed);
            Assert.IsNull(attempt.Diagnostic);
        }
        else
        {
            Assert.AreEqual(1, scope.Service.CaptureSnapshot().MonitorCounts.Active,
                "Withdrawing the collection must leave its retained monitor available.");
        }
    });

    private static async Task RunAsync(Func<Scenario, Task> body, CollectionClock? clock = null)
    {
        clock ??= new CollectionClock();
        var scope = new Scenario(clock);
        Exception? primary = null;
        try
        {
            Task start = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => scope.Runner.StartAsync());
            await start.WaitAsync(Deadline);
            // Finish the initial empty selection before creating any tested
            // handle, so startup cannot supply an unrelated strong reference.
            Assert.IsTrue(await clock.InitialTimerChange.Task.WaitAsync(Deadline));
            await body(scope);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            scope.Go.Set();
            try
            {
                await HealthCollectorTestCleanup.DrainAsync(Deadline, primary, [],
                    scope.JoinAsync, scope.ObserveOwnedCallAsync,
                    () => scope.RetainedAfterObservation?.DisposeAsync().AsTask() ?? Task.CompletedTask,
                    () => scope.Runner.DisposeAsync().AsTask(), () => scope.Service.DisposeAsync().AsTask());
            }
            finally
            {
                if (scope.Thread is null || !scope.Thread.IsAlive) { scope.DisposeEvents(); }
                GC.KeepAlive(scope);
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ActivateAsLastHandleUse(Scenario scope, CollectionClock clock)
    {
        IConditionMonitor monitor = scope.Service.CreateCondition(new() { IsCritical = true, InitiallyFlagged = false });
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, new Owner(), new() { Interval = TimeSpan.FromHours(1) });
        clock.Handle = new(registration);
        clock.MonitorId = monitor.MonitorId;
        clock.CallingThread = Environment.CurrentManagedThreadId;
        clock.Armed = 1;
        registration.ActivatePeriodic();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokeOwnedCall(Scenario scope, bool collect)
    {
        try
        {
            IConditionMonitor monitor = scope.Service.CreateCondition(new() { IsCritical = true, InitiallyFlagged = false });
            IHealthCollectionRegistration registration = scope.Runner.Register(monitor, new Owner(), new());
            scope.Handle = new(registration); scope.Monitor = new(monitor); scope.MonitorId = monitor.MonitorId;
            scope.Ready.Set();
            if (!scope.Go.Wait(Deadline)) { throw new TimeoutException("The fixture did not release owned public entry."); }
            scope.Entered.Set();
            if (collect) { scope.Caller = registration.CollectAsync(); }
            else { registration.Withdraw(); }
            scope.Returned.TrySetResult();
        }
        catch (Exception error) { scope.Returned.TrySetException(error); scope.Ready.Set(); }
    }

    private sealed class Scenario
    {
        internal Scenario(TimeProvider? clock)
        {
            Service = new(new(), new() { Name = "actual registration handle lifetime", InstanceId = Guid.NewGuid() }, clock);
            Runner = new(Service);
        }
        internal HealthcheckService Service { get; }
        internal HealthCollectorRunner Runner { get; }
        internal readonly ManualResetEventSlim Ready = new(), Go = new(), Entered = new();
        internal readonly TaskCompletionSource Returned = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Thread? Thread;
        private bool started;
        internal WeakReference? Handle, Monitor;
        internal Guid MonitorId;
        internal IHealthCollectionRegistration? RetainedAfterObservation;
        internal Task<HealthCollectionAttempt>? Caller;
        internal void StartOwnedCall(bool collect)
        {
            // This delegate retains only the scenario and a Boolean. The actual
            // handle is a local of the non-inlined caller, never a closure field.
            Thread = new(() => InvokeOwnedCall(this, collect)) { IsBackground = true };
            Thread.Start();
            started = true;
        }
        internal Task JoinAsync() => Task.Run(() =>
        {
            if (started) { Assert.IsTrue(Thread!.Join(Deadline), "Every owned public caller must actually exit."); }
        });
        internal async Task ObserveOwnedCallAsync()
        {
            if (!started) { return; }
            await Returned.Task.WaitAsync(Deadline);
            if (Caller is not null) { await Caller.WaitAsync(Deadline); }
        }
        internal void DisposeEvents() { Ready.Dispose(); Go.Dispose(); Entered.Dispose(); }
    }

    private sealed class CollectionClock : TimeProvider
    {
        internal TaskCompletionSource<bool> InitialTimerChange { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal HealthcheckService Service = null!;
        internal WeakReference Handle = null!;
        internal Guid MonitorId;
        internal int CallingThread, Armed, Callbacks, Collections;
        internal bool HandleAlive, MonitorPresent;
        public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;
        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow();
        public override long GetTimestamp()
        {
            if (Environment.CurrentManagedThreadId == CallingThread && Interlocked.Exchange(ref Armed, 0) == 1)
            {
                Callbacks++;
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                Collections++;
                HandleAlive = Handle.IsAlive;
                MonitorPresent = Service.CaptureSnapshot().Monitors.Any(monitor => monitor.MonitorId == MonitorId);
            }
            return TimeProvider.System.GetTimestamp();
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            new ObservedTimer(TimeProvider.System.CreateTimer(callback, state, dueTime, period), InitialTimerChange);

        private sealed class ObservedTimer(ITimer actual, TaskCompletionSource<bool> firstChange) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                bool changed = actual.Change(dueTime, period);
                firstChange.TrySetResult(changed);
                return changed;
            }
            public void Dispose() => actual.Dispose();
            public ValueTask DisposeAsync() => actual.DisposeAsync();
        }
    }

    private sealed class Owner : IHealthCollector
    {
        public bool TryAcquire(out IHealthCollectionLease? lease) { lease = new Lease(); return true; }
        private sealed class Lease : IHealthCollectionLease
        {
            public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation) => Task.FromResult(HealthCollectionResult.Condition(false));
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}

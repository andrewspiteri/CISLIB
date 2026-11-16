using System.Runtime.CompilerServices;
using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.Heartbeat.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Heartbeat;

[TestClass]
public sealed class HeartbeatHealthcheckIntegrationTests
{
    [TestMethod]
    public async Task SuccessfulConditionAndCallbackReachTheRealBinderWithDistinctActualWorkCompletion()
    {
        await using var scope = new HeartbeatTestScope();
        await using HealthcheckService health = Health(scope.Clock);
        await using IConsecutiveFailureMonitor monitor = Monitor(health);
        var subscriber = new HeartbeatProbe
        {
            Callback = _ => { scope.Clock.Advance(TimeSpan.FromMilliseconds(3)); return Task.FromResult(true); }
        };
        scope.Owner.Service.Register(subscriber, new(), Monitoring(monitor), _ =>
        {
            scope.Clock.Advance(TimeSpan.FromMilliseconds(2));
            return Task.FromResult(true);
        });
        scope.Activate();
        Task unregister = scope.Owner.Service.UnregisterAsync(subscriber);
        scope.Executor.RunNext();
        await unregister.WaitAsync(HeartbeatTestScope.Deadline);

        HealthOutcomeSnapshot outcomes = Snapshot(health, monitor).Outcomes!;
        HealthExecutionSnapshot execution = outcomes.Execution!;
        Assert.AreEqual(1L, outcomes.Successful);
        Assert.AreEqual(0L, outcomes.Failed);
        Assert.AreEqual(true, execution.ConditionResult);
        Assert.AreEqual(true, execution.CallbackResult);
        Assert.AreEqual(2d, execution.ConditionDurationMilliseconds);
        Assert.AreEqual(3d, execution.CallbackDurationMilliseconds);
        Assert.AreEqual(execution.CallbackStartedAtUtc!.Value.AddMilliseconds(3), execution.WorkCompletedAtUtc);
        Assert.AreEqual(HealthExecutionPhase.Settled, execution.Phase);
        Assert.IsNotNull(execution.DispatchedAtUtc);
        Assert.IsNull(execution.IntegrationError);
        Assert.IsFalse(scope.Runtime.InfrastructureFailure.IsCompleted, "Valid phase and completion facts must not contradict the real receiver.");
        Assert.AreEqual(1, subscriber.Calls);
        Assert.AreEqual(0, subscriber.Errors);
    }

    [TestMethod]
    public async Task AnExecutorStartingBeforeTryPostReturnsCannotRunUserWorkBeforeDispatchPublication()
    {
        var clock = new HeartbeatTestClock();
        using var executor = new BeforePostReturnsExecutor();
        await using HealthcheckService health = Health(clock);
        await using IConsecutiveFailureMonitor monitor = Monitor(health);
        var runtime = new HeartbeatRuntime(new HeartbeatSettings(1), clock, executor);
        HeartbeatOwner owner = runtime.CreateOwner();
        var subscriber = new HeartbeatProbe();
        try
        {
            owner.Service.Register(subscriber, new(), Monitoring(monitor));
            runtime.Activate();
            await executor.WrapperEntered.Task.WaitAsync(HeartbeatTestScope.Deadline);
            Assert.AreEqual(0, subscriber.Calls);
            Assert.AreEqual(1, runtime.Inspect().Dispatched);
            HealthExecutionSnapshot admitted = Snapshot(health, monitor).Outcomes!.Execution!;
            Assert.AreEqual(HealthExecutionPhase.Admitted, admitted.Phase);
            Assert.IsNull(admitted.DispatchedAtUtc, "A post still awaiting its return cannot publish successful dispatch metadata.");
            Task unregister = owner.Service.UnregisterAsync(subscriber);
            Assert.IsFalse(unregister.IsCompleted);
            executor.AllowReturn.Set();
            await unregister.WaitAsync(HeartbeatTestScope.Deadline);
            HealthOutcomeSnapshot outcomes = Snapshot(health, monitor).Outcomes!;
            Assert.AreEqual(1L, outcomes.Successful);
            Assert.AreEqual(HealthExecutionPhase.Settled, outcomes.Execution!.Phase);
            Assert.IsNotNull(outcomes.Execution.DispatchedAtUtc);
            Assert.AreEqual(1, subscriber.Calls);
            Assert.IsFalse(runtime.InfrastructureFailure.IsCompleted);
        }
        finally
        {
            executor.AllowReturn.Set();
            await runtime.StopAsync().WaitAsync(HeartbeatTestScope.Deadline);
            await owner.DisposeAsync();
            await runtime.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task DuplicateRegistrationAndDuplicateWhileDrainingBindOnceAndCompletedReuseBindsFreshIdentity()
    {
        await using var scope = new HeartbeatTestScope();
        await using HealthcheckService health = Health(scope.Clock);
        TaskCompletionSource<bool> work = scope.HoldResult();
        var subscriber = new HeartbeatProbe { Callback = _ => work.Task };
        var selected = new HeartbeatHealthcheckOptions
        {
            MonitorFactory = health,
            ConsecutiveFailures = new() { IsCritical = true, FailureThreshold = 2 }
        };
        scope.Owner.Service.Register(subscriber, new(), selected);
        Guid originalMonitor = health.CaptureSnapshot().Monitors.Single().MonitorId;
        scope.Owner.Service.Register(subscriber, new() { InitialDelay = TimeSpan.FromHours(1) }, new HeartbeatHealthcheckOptions
        {
            MonitorFactory = health,
            Availability = new() { IsCritical = false },
            SuccessPublishesUsableData = true
        });
        Assert.AreEqual(1, health.CaptureSnapshot().MonitorCounts.Active);
        scope.Activate();
        scope.Executor.RunNext();
        Guid originalRegistration = health.CaptureSnapshot().Monitors.Single().Outcomes!.Execution!.RegistrationId;
        Task unregister = scope.Owner.Service.UnregisterAsync(subscriber);
        scope.Owner.Service.Register(subscriber, new(), selected);
        Assert.IsFalse(unregister.IsCompleted);
        Assert.AreEqual(originalMonitor, health.CaptureSnapshot().Monitors.Single().MonitorId);
        work.TrySetResult(true);
        await unregister.WaitAsync(HeartbeatTestScope.Deadline);
        Assert.AreEqual(0, health.CaptureSnapshot().MonitorCounts.Active, "The first binding owns and withdraws its factory-created monitor.");

        subscriber.Callback = static _ => Task.FromResult(true);
        scope.Owner.Service.Register(subscriber, new(), selected);
        HeartbeatTestScope.WaitUntil(() => scope.Executor.PendingCount == 1, "Sequential reuse did not dispatch its new registration.");
        scope.Executor.RunNext();
        HeartbeatTestScope.WaitUntil(() => health.CaptureSnapshot().Monitors.Single().Outcomes!.Execution?.SettledAtUtc is not null,
            "The replacement registration did not settle through the binder.");
        HealthMonitorSnapshot replacement = health.CaptureSnapshot().Monitors.Single();
        Assert.AreNotEqual(originalMonitor, replacement.MonitorId);
        Assert.AreNotEqual(originalRegistration, replacement.Outcomes!.Execution!.RegistrationId);
        Assert.AreEqual(1L, replacement.Outcomes.Successful);
        Assert.AreEqual(2, subscriber.Calls);
        Assert.IsFalse(scope.Runtime.InfrastructureFailure.IsCompleted);
        await scope.Owner.Service.UnregisterAsync(subscriber).WaitAsync(HeartbeatTestScope.Deadline);
    }

    [TestMethod]
    public async Task GlobalShutdownReportsNeutralSuppressionForTheRealPostedWrapper()
    {
        await using var scope = new HeartbeatTestScope();
        await using HealthcheckService health = Health(scope.Clock);
        await using IConsecutiveFailureMonitor monitor = Monitor(health);
        var subscriber = new HeartbeatProbe();
        int conditions = 0;
        scope.Owner.Service.Register(subscriber, new(), Monitoring(monitor), _ =>
        {
            conditions++;
            return Task.FromResult(true);
        });
        scope.Activate();
        Task stop = scope.Runtime.StopAsync();
        Assert.IsFalse(stop.IsCompleted);
        scope.Executor.RunNext();
        await stop.WaitAsync(HeartbeatTestScope.Deadline);

        HealthOutcomeSnapshot outcomes = Snapshot(health, monitor).Outcomes!;
        Assert.AreEqual(0, conditions);
        Assert.AreEqual(0, subscriber.Calls);
        Assert.AreEqual(0L, outcomes.Successful + outcomes.Failed + outcomes.Skipped + outcomes.ExpectedCanceled);
        Assert.AreEqual(HealthExecutionSuppressionReason.ApplicationStopping, outcomes.Execution!.SuppressionReason);
        Assert.AreEqual(HealthExecutionPhase.Condition, outcomes.Execution.SuppressedPhase);
        Assert.AreEqual(HealthExecutionPhase.Settled, outcomes.Execution.Phase);
        Assert.IsNull(outcomes.Execution.ConditionStartedAtUtc);
        Assert.IsNull(outcomes.Execution.CallbackStartedAtUtc);
        Assert.IsFalse(scope.Runtime.InfrastructureFailure.IsCompleted);
        Assert.AreEqual(1, health.CaptureSnapshot().MonitorCounts.Active, "Retiring an external binding must preserve its monitor.");
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task RealRunningConditionRetainsItsBooleanAcrossTheCallbackShutdownGate(bool conditionResult)
    {
        await using var scope = new HeartbeatTestScope();
        await using HealthcheckService health = Health(scope.Clock);
        await using IConsecutiveFailureMonitor monitor = Monitor(health);
        TaskCompletionSource<bool> condition = scope.HoldResult();
        var subscriber = new HeartbeatProbe();
        scope.Owner.Service.Register(subscriber, new(), Monitoring(monitor), _ => condition.Task);
        scope.Activate();
        scope.Executor.RunNext();
        HeartbeatTestScope.WaitUntil(() => Snapshot(health, monitor).Outcomes!.Execution?.Phase == HealthExecutionPhase.Condition,
            "The condition must actually start before the shutdown cutoff is tested.");
        Task stop = scope.Runtime.StopAsync();
        Assert.IsFalse(stop.IsCompleted);
        scope.Clock.Advance(TimeSpan.FromMilliseconds(2));
        condition.TrySetResult(conditionResult);
        await stop.WaitAsync(HeartbeatTestScope.Deadline);

        HealthOutcomeSnapshot outcomes = Snapshot(health, monitor).Outcomes!;
        Assert.AreEqual(0, subscriber.Calls);
        Assert.AreEqual(conditionResult, outcomes.Execution!.ConditionResult);
        Assert.IsNull(outcomes.Execution.CallbackResult);
        Assert.IsNull(outcomes.Execution.CallbackStartedAtUtc);
        Assert.AreEqual(0L, outcomes.Failed);
        Assert.AreEqual(conditionResult ? 0L : 1L, outcomes.Skipped);
        Assert.AreEqual(conditionResult ? HealthExecutionPhase.Callback : (HealthExecutionPhase?)null, outcomes.Execution.SuppressedPhase);
        Assert.AreEqual(HealthExecutionPhase.Settled, outcomes.Execution.Phase);
        Assert.IsFalse(scope.Runtime.InfrastructureFailure.IsCompleted);
    }

    [TestMethod]
    public async Task ActualCleanupFailurePreservesTrueCallbackAndCountsExactlyOneFinalFailure()
    {
        await using var scope = new HeartbeatTestScope();
        await using HealthcheckService health = Health(scope.Clock);
        await using IConsecutiveFailureMonitor monitor = Monitor(health);
        var subscriber = new HeartbeatProbe();
        scope.Runtime.Register(scope.Owner, subscriber, new(), observerFactory: id => new HeartbeatHealthcheckObserver(
                HealthExecutionBinder.Bind(monitor, new() { RegistrationId = id })),
            cleanup: static () => ValueTask.FromException(new IOException("owned cleanup failed")));
        scope.Activate();
        Task unregister = scope.Owner.Service.UnregisterAsync(subscriber);
        scope.Executor.RunNext();
        await unregister.WaitAsync(HeartbeatTestScope.Deadline);

        HealthOutcomeSnapshot outcomes = Snapshot(health, monitor).Outcomes!;
        Assert.AreEqual(1, subscriber.Calls);
        Assert.AreEqual(0, subscriber.Errors);
        Assert.AreEqual(0L, outcomes.Successful);
        Assert.AreEqual(1L, outcomes.Failed);
        Assert.AreEqual(HealthExecutionOutcome.Failed, outcomes.LastOutcome);
        Assert.AreEqual(true, outcomes.Execution!.CallbackResult);
        Assert.AreEqual(HealthExecutionStepStatus.Failed, outcomes.Execution.CleanupStatus);
        Assert.AreEqual(typeof(IOException).FullName, outcomes.Execution.CleanupError!.ExceptionType);
        Assert.IsNull(outcomes.Execution.OriginalError);
        Assert.AreEqual(HealthExecutionPhase.Settled, outcomes.Execution.Phase);
        HeartbeatInfrastructureFault fault = await scope.Runtime.InfrastructureFailure.WaitAsync(HeartbeatTestScope.Deadline);
        Assert.AreEqual("Heartbeat.ExecutionCleanup", fault.Code);
    }

    [TestMethod]
    [DataRow("bound")]
    [DataRow("capturing")]
    public async Task SurvivingRealBindingAndExternalMonitorCannotRootTheIdleTargetOrOwnerAndRetireOnce(string conditionKind)
    {
        await using var scope = new HeartbeatTestScope();
        await using HealthcheckService health = Health(scope.Clock);
        await using IConsecutiveFailureMonitor monitor = Monitor(health);
        IdleGraph graph = RegisterIdleGraph(scope.Runtime, monitor, conditionKind);
        for (int attempt = 0; attempt < 12 && (IsAlive(graph.Subscriber) || IsAlive(graph.Owner)); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Assert.IsFalse(IsAlive(graph.Subscriber), "The real binder or condition rooted the idle subscriber.");
        Assert.IsFalse(IsAlive(graph.Owner), "The real binder retained the subscriber's actual owner.");
        Assert.AreEqual(1, health.CaptureSnapshot().MonitorCounts.Active);
        scope.Runtime.Activate();
        HeartbeatTestScope.WaitUntil(() => scope.Runtime.Inspect().Registrations == 0, "Collection did not finish the real binding retirement barrier.");
        await scope.Runtime.StopAsync().WaitAsync(HeartbeatTestScope.Deadline);
        Assert.AreEqual(1, graph.Observer.Retirements);
        Assert.AreEqual(1, graph.Observer.Disposals);
        Assert.AreEqual(0, scope.Executor.Posts);
        Assert.AreEqual(1, health.CaptureSnapshot().MonitorCounts.Active);
        Assert.IsNull(Snapshot(health, monitor).Outcomes!.Execution);
        Assert.IsFalse(scope.Runtime.InfrastructureFailure.IsCompleted);
        monitor.ReportSuccess();
        Assert.AreEqual(1L, Snapshot(health, monitor).Outcomes!.Successful);
        GC.KeepAlive(graph.Observer);
        GC.KeepAlive(monitor);
    }

    private static HeartbeatHealthcheckOptions Monitoring(IHealthMonitor monitor) => new() { Monitor = monitor };
    private static IConsecutiveFailureMonitor Monitor(HealthcheckService health) => health.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 2 });
    private static HealthMonitorSnapshot Snapshot(HealthcheckService health, IHealthMonitor monitor)
        => health.CaptureSnapshot().Monitors.Single(item => item.MonitorId == monitor.MonitorId);
    private static HealthcheckService Health(TimeProvider clock) => new(new HealthcheckOptions(),
        new HealthApplicationIdentity { Name = "heartbeat-healthcheck-tests", InstanceId = Guid.NewGuid() }, clock);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IdleGraph RegisterIdleGraph(HeartbeatRuntime runtime, IHealthMonitor monitor, string conditionKind)
    {
        HeartbeatOwner owner = runtime.CreateOwner();
        var subscriber = new OwnedHealthProbe(owner);
        RetirementObserver? observer = null;
        Func<CancellationToken, Task<bool>> condition = conditionKind == "bound" ? subscriber.ConditionAsync
            : _ => Task.FromResult(subscriber.Owner.Service is not null);
        runtime.Register(owner, subscriber, new(), condition, id =>
        {
            observer = new(new HeartbeatHealthcheckObserver(HealthExecutionBinder.Bind(monitor, new() { RegistrationId = id })));
            return observer;
        });
        Assert.IsNotNull(observer);
        return new(new WeakReference<HeartbeatProbe>(subscriber), new WeakReference<HeartbeatOwner>(owner), observer);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsAlive<T>(WeakReference<T> reference) where T : class => reference.TryGetTarget(out _);

    private sealed class OwnedHealthProbe(HeartbeatOwner owner) : HeartbeatProbe
    {
        internal HeartbeatOwner Owner { get; } = owner;
        internal Task<bool> ConditionAsync(CancellationToken _) => Task.FromResult(Owner.Service is not null);
    }

    private sealed record IdleGraph(WeakReference<HeartbeatProbe> Subscriber, WeakReference<HeartbeatOwner> Owner, RetirementObserver Observer);

    private sealed class RetirementObserver(IHeartbeatObserver actual) : IHeartbeatObserver
    {
        private int retirements, disposals;
        internal int Retirements => Volatile.Read(ref retirements);
        internal int Disposals => Volatile.Read(ref disposals);
        public void Report(HeartbeatNotification fact)
        {
            actual.Report(fact);
            if (fact.Kind == HeartbeatEvent.Retired) { Interlocked.Increment(ref retirements); }
        }
        public ValueTask FlushAsync() => actual.FlushAsync();
        public ValueTask DisposeAsync() { Interlocked.Increment(ref disposals); return actual.DisposeAsync(); }
    }

    private sealed class BeforePostReturnsExecutor : IHeartbeatExecutor, IDisposable
    {
        internal TaskCompletionSource WrapperEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim AllowReturn { get; } = new();
        public bool TryPost(Action start)
        {
            start();
            WrapperEntered.TrySetResult();
            if (!AllowReturn.Wait(HeartbeatTestScope.Deadline)) { throw new TimeoutException("The test did not release the post-return gate."); }
            return true;
        }
        public void Dispose() => AllowReturn.Dispose();
    }
}

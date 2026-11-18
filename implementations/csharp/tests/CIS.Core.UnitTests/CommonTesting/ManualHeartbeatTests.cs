using CIS.CommonTesting;
using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.Heartbeat.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.CommonTesting;

[TestClass]
public sealed class ManualHeartbeatTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [TestMethod]
    public async Task PublicMonitoredRegistrationReportsActualResultsAndKeepsSuppliedMonitorCallerOwned()
    {
        await using var manual = new ManualHeartbeat();
        var health = new RecordingHealthMonitorFactory();
        await using IAvailabilityMonitor monitor = health.CreateAvailability(new()
        {
            IsCritical = true
        });
        IHeartbeatOwner owner = manual.CreateOwner();
        var subscriber = new Subscriber(_ => Task.FromResult(true));
        var schedule = new HeartbeatRegistrationOptions { InitialDelay = TimeSpan.FromHours(1), Interval = TimeSpan.FromHours(1) };
        owner.Service.Register(subscriber, schedule, new HeartbeatHealthcheckOptions
        {
            Monitor = monitor,
            SuccessPublishesUsableData = true
        });
        owner.Service.Register(subscriber, schedule, new HeartbeatHealthcheckOptions
        {
            Monitor = monitor,
            SuccessPublishesUsableData = true
        });
        Assert.HasCount(1, manual.Registrations);
        Assert.AreEqual(TimeSpan.FromHours(1), manual.Registrations[0].Options.InitialDelay);
        Assert.AreSame(subscriber, manual.Registrations[0].Subscriber);
        Assert.AreEqual(0, subscriber.Calls);
        ManualHeartbeatResult result = await manual.TriggerAsync(subscriber);
        Assert.AreEqual(ManualHeartbeatOutcome.Succeeded, result.Outcome);
        Assert.AreEqual(true, result.CallbackResult);
        Assert.AreEqual(1L, health.Monitors[0].Count(HealthReportKind.Published));
        await owner.CloseAsync().WaitAsync(Deadline);
        Assert.HasCount(0, manual.Registrations);
        Assert.IsFalse(health.Monitors[0].IsWithdrawn);
    }

    [TestMethod]
    public async Task CanceledCloseWaitKeepsActualAcceptedWorkAliveAndRejectsNewAdmission()
    {
        await using var manual = new ManualHeartbeat();
        IHeartbeatOwner owner = manual.CreateOwner();
        var entered = Signal();
        var release = Signal();
        var subscriber = new Subscriber(async token =>
        {
            entered.TrySetResult();
            await release.Task;
            Assert.IsFalse(token.IsCancellationRequested);
            return true;
        });
        owner.Service.Register(subscriber, new());
        Task<ManualHeartbeatResult> invocation = manual.TriggerAsync(subscriber);
        try
        {
            await entered.Task.WaitAsync(Deadline);
            using var cancellation = new CancellationTokenSource();
            Task wait = owner.CloseAsync(cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => wait);
            Assert.IsFalse(invocation.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => owner.Service.Register(subscriber, new()));
            Assert.Throws<InvalidOperationException>(() => manual.TriggerAsync(subscriber));
            release.TrySetResult();
            await owner.CloseAsync().WaitAsync(Deadline);
            Assert.IsTrue(invocation.IsCompleted);
            Assert.AreEqual(ManualHeartbeatOutcome.Succeeded, (await invocation).Outcome);
        }
        finally
        {
            release.TrySetResult();
            await invocation.WaitAsync(Deadline);
            await owner.CloseAsync().WaitAsync(Deadline);
        }
    }

    [TestMethod]
    public async Task UnregisterJoinsAwaitedOnErrorAndDisposesOnlyTheCreatedPolicyMonitor()
    {
        await using var manual = new ManualHeartbeat();
        IHeartbeatOwner owner = manual.CreateOwner();
        var health = new RecordingHealthMonitorFactory();
        var original = new InvalidOperationException("user phase");
        var entered = Signal();
        var release = Signal();
        var subscriber = new Subscriber(_ => Task.FromException<bool>(original), async error =>
        {
            Assert.AreSame(original, error);
            entered.TrySetResult();
            await release.Task;
        });
        owner.Service.Register(subscriber, new(), new HeartbeatHealthcheckOptions
        {
            MonitorFactory = health,
            ConsecutiveFailures = new() { IsCritical = true, FailureThreshold = 2 }
        });
        Task<ManualHeartbeatResult> invocation = manual.TriggerAsync(subscriber);
        Task? drain = null;
        try
        {
            await entered.Task.WaitAsync(Deadline);
            drain = owner.Service.UnregisterAsync(subscriber);
            Assert.IsFalse(drain.IsCompleted);
            Assert.IsFalse(health.Monitors[0].IsWithdrawn);
            release.TrySetResult();
            ManualHeartbeatResult result = await invocation.WaitAsync(Deadline);
            await drain.WaitAsync(Deadline);
            Assert.AreSame(original, result.Error);
            Assert.AreEqual(1L, health.Monitors[0].Count(HealthReportKind.Failure));
            Assert.AreEqual(1L, health.Monitors[0].Count(HealthReportKind.Withdrawn));
        }
        finally
        {
            release.TrySetResult();
            await invocation.WaitAsync(Deadline);
            if (drain is not null)
            {
                await drain.WaitAsync(Deadline);
            }
        }
    }

    [TestMethod]
    public async Task AFalseConditionSkipsCallbackAndAReentrantCloseCannotCertifyItsOwnWork()
    {
        await using var manual = new ManualHeartbeat();
        IHeartbeatOwner owner = manual.CreateOwner();
        var subscriber = new Subscriber(_ => Task.FromResult(true));
        owner.Service.Register(subscriber, new(), _ => Task.FromResult(false));
        Assert.AreEqual(ManualHeartbeatOutcome.Skipped, (await manual.TriggerAsync(subscriber)).Outcome);
        Assert.AreEqual(0, subscriber.Calls);
        await owner.Service.UnregisterAsync(subscriber);
        var selfClose = new Subscriber(_ =>
        {
            Assert.Throws<InvalidOperationException>(() => owner.CloseAsync());
            return Task.FromResult(true);
        });
        owner.Service.Register(selfClose, new());
        Task<ManualHeartbeatResult> actual = manual.TriggerAsync(selfClose);
        await owner.CloseAsync().WaitAsync(Deadline);
        Assert.IsTrue(actual.IsCompleted);
    }

    [TestMethod]
    public async Task ExplicitCancellationIsNeutralAndConcurrentManualInvocationsAreRejected()
    {
        await using var manual = new ManualHeartbeat();
        IHeartbeatOwner owner = manual.CreateOwner();
        using var cancellation = new CancellationTokenSource();
        var entered = Signal();
        var subscriber = new Subscriber(async token =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return true;
        });
        owner.Service.Register(subscriber, new());
        Task<ManualHeartbeatResult> actual = manual.TriggerAsync(subscriber, cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(Deadline);
            Assert.Throws<InvalidOperationException>(() => manual.TriggerAsync(subscriber));
            cancellation.Cancel();
            Assert.AreEqual(ManualHeartbeatOutcome.ExpectedCancellation, (await actual.WaitAsync(Deadline)).Outcome);
            Assert.AreEqual(0, subscriber.Errors);
        }
        finally
        {
            cancellation.Cancel();
            await actual.WaitAsync(Deadline);
        }
    }

    private sealed class Subscriber(Func<CancellationToken, Task<bool>> callback, Func<Exception, Task>? error = null) : IHeartbeatSubscriber
    {
        internal int Calls
        {
            get; private set;
        }
        internal int Errors
        {
            get; private set;
        }
        public Task<bool> OnElapsedAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return callback(cancellationToken);
        }
        public Task OnErrorAsync(Exception exception)
        {
            Errors++;
            return error?.Invoke(exception) ?? Task.CompletedTask;
        }
    }
}

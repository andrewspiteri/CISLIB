using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.Startup;
using CIS.Tests.UnitTests.Heartbeat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Startup;

[TestClass]
public sealed class StartupHeartbeatCompletionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ActivationFailureOrStoppedNoOpCannotPublishStartupSuccess(bool alreadyStopped)
    {
        await using var health = new HealthcheckService(new());
        var services = new ServiceCollection();
        var participant = new StartupProbe();
        services.AddSingleton<IStartable>(participant);
        await using ServiceProvider provider = StartupTestSupport.BuildProvider(services);
        var executor = new HeartbeatControlledExecutor();
        await using var heartbeat = new HeartbeatRuntime(new HeartbeatSettings(1), new ActivationClock(fail: !alreadyStopped), executor);
        if (alreadyStopped) { await heartbeat.StopAsync().WaitAsync(StartupTestSupport.Deadline); }
        await using var coordinator = new StartupCoordinator(provider, services.ToArray(), health, heartbeat);

        Exception error = await StartupTestSupport.ObserveFailureAsync(coordinator.StartAsync(CancellationToken.None));
        Assert.IsInstanceOfType<InvalidOperationException>(error);
        Assert.AreEqual(1, participant.Calls, "The failure belongs to activation after actual initialization succeeded.");
        Assert.IsFalse(health.CaptureSnapshot().Application.StartupCompleted);
        Assert.AreEqual(1, health.CaptureSnapshot().MonitorCounts.CriticalFailing);
        Assert.AreEqual(0, executor.Posts);
        await coordinator.StopAsync().WaitAsync(StartupTestSupport.Deadline);
        if (!alreadyStopped) { Assert.IsTrue(heartbeat.InfrastructureFailure.IsCompletedSuccessfully); }
    }

    [TestMethod]
    public async Task HostCancellationDuringActualTimerCreationCannotPublishOrDispatch()
    {
        using var startupCancellation = new CancellationTokenSource();
        await using var health = new HealthcheckService(new());
        var services = new ServiceCollection();
        var participant = new StartupProbe();
        services.AddSingleton<IStartable>(participant);
        await using ServiceProvider provider = StartupTestSupport.BuildProvider(services);
        var executor = new HeartbeatControlledExecutor();
        await using var heartbeat = new HeartbeatRuntime(new HeartbeatSettings(1),
            new ActivationClock(beforeTimer: startupCancellation.Cancel), executor);
        HeartbeatOwner owner = heartbeat.CreateOwner();
        var subscriber = new HeartbeatProbe();
        owner.Service.Register(subscriber, new());
        await using var coordinator = new StartupCoordinator(provider, services.ToArray(), health, heartbeat);

        Task startup = coordinator.StartAsync(startupCancellation.Token);
        Exception error = await StartupTestSupport.ObserveFailureAsync(startup);
        Assert.IsInstanceOfType<OperationCanceledException>(error);
        Assert.IsTrue(startup.IsCanceled);
        Assert.AreEqual(1, participant.Calls);
        Assert.IsFalse(health.CaptureSnapshot().Application.StartupCompleted);
        Assert.AreEqual(0, health.CaptureSnapshot().MonitorCounts.CriticalFailing, "Known host cancellation stays neutral.");
        await coordinator.StopAsync().WaitAsync(StartupTestSupport.Deadline);
        Assert.AreEqual(0, executor.Posts);
        Assert.AreEqual(0, subscriber.Calls);
        await owner.CloseAsync().WaitAsync(StartupTestSupport.Deadline);
    }

    private sealed class ActivationClock(bool fail = false, Action? beforeTimer = null) : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (fail) { throw new InvalidOperationException("Controlled timer creation failure."); }
            beforeTimer?.Invoke();
            return System.CreateTimer(callback, state, dueTime, period);
        }
    }
}

using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.Heartbeat.Hosting;
using CIS.Startup;
using CIS.Startup.Hosting;
using CIS.Tests.UnitTests.Heartbeat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
        var participant = new StartupProbe();
        var executor = new HeartbeatControlledExecutor();
        await using var heartbeat = new HeartbeatRuntime(new HeartbeatSettings(1), new ActivationClock(fail: !alreadyStopped), executor);
        if (alreadyStopped) { await heartbeat.StopAsync().WaitAsync(StartupTestSupport.Deadline); }
        IHost host = CreateHost(health, heartbeat, participant);
        Task? stop = null;
        try
        {
            Exception error = await StartupTestSupport.ObserveFailureAsync(host.StartAsync(CancellationToken.None));
            Assert.IsInstanceOfType<InvalidOperationException>(error);
            Assert.AreEqual(1, participant.Calls, "The failure belongs to activation after actual initialization succeeded.");
            Assert.IsFalse(health.CaptureSnapshot().Application.StartupCompleted);
            Assert.AreEqual(1, health.CaptureSnapshot().MonitorCounts.CriticalFailing);
            Assert.AreEqual(0, executor.Posts);
            stop = host.StopAsync(CancellationToken.None);
            await stop.WaitAsync(StartupTestSupport.Deadline);
            if (!alreadyStopped) { Assert.IsTrue(heartbeat.InfrastructureFailure.IsCompletedSuccessfully); }
        }
        finally { await StopAndDisposeHostAsync(host, stop); }
    }

    [TestMethod]
    public async Task HostCancellationDuringActualTimerCreationCannotPublishOrDispatch()
    {
        using var startupCancellation = new CancellationTokenSource();
        await using var health = new HealthcheckService(new());
        var participant = new StartupProbe();
        var executor = new HeartbeatControlledExecutor();
        await using var heartbeat = new HeartbeatRuntime(new HeartbeatSettings(1),
            new ActivationClock(beforeTimer: startupCancellation.Cancel), executor);
        await using HeartbeatOwner owner = heartbeat.CreateOwner();
        var subscriber = new HeartbeatProbe();
        owner.Service.Register(subscriber, new());
        IHost host = CreateHost(health, heartbeat, participant);
        Task? stop = null;
        try
        {
            Task startup = host.StartAsync(startupCancellation.Token);
            Exception error = await StartupTestSupport.ObserveFailureAsync(startup);
            Assert.IsInstanceOfType<OperationCanceledException>(error);
            Assert.IsTrue(startup.IsCanceled);
            Assert.AreEqual(1, participant.Calls);
            Assert.IsFalse(health.CaptureSnapshot().Application.StartupCompleted);
            Assert.AreEqual(0, health.CaptureSnapshot().MonitorCounts.CriticalFailing, "Known host cancellation stays neutral.");
            stop = host.StopAsync(CancellationToken.None);
            await stop.WaitAsync(StartupTestSupport.Deadline);
            Assert.AreEqual(0, executor.Posts);
            Assert.AreEqual(0, subscriber.Calls);
            await owner.CloseAsync().WaitAsync(StartupTestSupport.Deadline);
        }
        finally { await StopAndDisposeHostAsync(host, stop); }
    }

    [TestMethod]
    public async Task FirstHostStartupCancellationClosesHeartbeatAdmissionWhileAnIgnoringInitializerRemainsActive()
    {
        using var startupCancellation = new CancellationTokenSource();
        await using var health = new HealthcheckService(new());
        var entered = StartupTestSupport.Signal();
        var release = StartupTestSupport.Signal();
        var participant = new StartupProbe
        {
            Start = async _ => { entered.TrySetResult(); await release.Task; }
        };
        var executor = new HeartbeatControlledExecutor();
        await using var heartbeat = new HeartbeatRuntime(new HeartbeatSettings(1), new ActivationClock(), executor);
        await using HeartbeatOwner owner = heartbeat.CreateOwner();
        var registered = new HeartbeatProbe();
        var fresh = new HeartbeatProbe();
        owner.Service.Register(registered, new());
        IHost host = CreateHost(health, heartbeat, participant);
        IHostApplicationLifetime lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        Task? startup = null, stop = null;
        try
        {
            startup = host.StartAsync(startupCancellation.Token);
            await entered.Task.WaitAsync(StartupTestSupport.Deadline);
            startupCancellation.Cancel();

            Assert.IsFalse(lifetime.ApplicationStopping.IsCancellationRequested,
                "Only the first host startup token was canceled; ApplicationStopping must not supply the cutoff.");
            Assert.IsFalse(startup.IsCompleted, "The initializer deliberately ignores cancellation until its real task is released.");
            Assert.ThrowsExactly<InvalidOperationException>(() => heartbeat.CreateOwner());
            Assert.ThrowsExactly<InvalidOperationException>(() => owner.Service.Register(fresh, new()));
            Assert.ThrowsExactly<InvalidOperationException>(() => owner.Service.Register(registered, new()));
            HealthSnapshot snapshot = health.CaptureSnapshot();
            Assert.IsFalse(snapshot.Application.StartupCompleted);
            Assert.IsFalse(snapshot.Ready);
            Assert.AreEqual(0, snapshot.MonitorCounts.CriticalFailing, "Known startup cancellation remains neutral.");
            Assert.AreEqual(0, executor.Posts);

            stop = host.StopAsync(CancellationToken.None);
            Assert.IsFalse(stop.IsCompleted, "Closing admission cannot certify the held initializer's settlement.");
            Assert.IsFalse(startup.IsCompleted);
            release.TrySetResult();
            Assert.IsInstanceOfType<OperationCanceledException>(await StartupTestSupport.ObserveFailureAsync(startup));
            Assert.IsTrue(startup.IsCanceled);
            await stop.WaitAsync(StartupTestSupport.Deadline);
            await owner.CloseAsync().WaitAsync(StartupTestSupport.Deadline);
            Assert.AreEqual(1, participant.Calls);
            Assert.AreEqual(0, registered.Calls);
            Assert.AreEqual(0, fresh.Calls);
            Assert.AreEqual(0, executor.Posts);
            Assert.IsFalse(health.CaptureSnapshot().Application.StartupCompleted);
            Assert.AreEqual(0, health.CaptureSnapshot().MonitorCounts.CriticalFailing);
        }
        finally
        {
            // Establish the cutoff before releasing user work even when an assertion fails.
            stop ??= host.StopAsync(CancellationToken.None);
            release.TrySetResult();
            if (startup is not null)
            {
                try { await startup.WaitAsync(StartupTestSupport.Deadline); }
                catch (Exception) when (startup.IsCompleted) { /* Its expected outcome is asserted above. */ }
            }
            await StopAndDisposeHostAsync(host, stop);
        }
    }

    private static IHost CreateHost(HealthcheckService health, HeartbeatRuntime heartbeat, StartupProbe participant)
        => Host.CreateDefaultBuilder()
            .UseDefaultServiceProvider(options => { options.ValidateScopes = true; options.ValidateOnBuild = true; })
            .ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureServices(services =>
            {
                services.AddSingleton(health);
                services.AddSingleton(heartbeat);
                services.AddSingleton<IStartable>(participant);
                services.AddCisHeartbeat();
                services.AddCisStartup();
            })
            .Build();

    private static async Task StopAndDisposeHostAsync(IHost host, Task? stopping)
    {
        // Native hosted adapters must prove the actual barrier before root disposal.
        await (stopping ?? host.StopAsync(CancellationToken.None)).WaitAsync(StartupTestSupport.Deadline);
        if (host is IAsyncDisposable asynchronous) { await asynchronous.DisposeAsync().AsTask().WaitAsync(StartupTestSupport.Deadline); }
        else { host.Dispose(); }
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

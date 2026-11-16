using CIS.Healthcheck;
using CIS.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Startup;

[TestClass]
public sealed class StartupCoordinatorCancellationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task APreCanceledFirstHostTokenRetainsCancellationWithoutResolvingParticipants(bool applicationStopping)
    {
        await using var health = new HealthcheckService(new());
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        int resolutions = 0;
        var participant = new StartupProbe();
        var services = new ServiceCollection();
        services.AddSingleton<IStartable>(_ => { resolutions++; return participant; });
        ServiceProvider provider = StartupTestSupport.BuildProvider(services);
        var coordinator = new StartupCoordinator(provider, services.ToArray(), health);
        try
        {
            Task operation = coordinator.StartAsync(
                applicationStopping ? CancellationToken.None : canceled.Token,
                applicationStopping ? canceled.Token : CancellationToken.None);
            Assert.IsInstanceOfType<OperationCanceledException>(await StartupTestSupport.ObserveFailureAsync(operation));
            Assert.IsTrue(operation.IsCanceled);
            Assert.AreEqual(0, resolutions);
            Assert.AreEqual(0, participant.Calls);
            Assert.IsFalse(health.CaptureSnapshot().Application.StartupCompleted);
            Assert.IsFalse(health.CaptureSnapshot().Ready);
            Assert.AreEqual(0, health.CaptureSnapshot().MonitorCounts.CriticalFailing);
            Assert.AreSame(operation, coordinator.StartAsync(CancellationToken.None));
            Assert.AreEqual(0, resolutions);
        }
        finally { await StartupTestSupport.StopAndDisposeAsync(coordinator, provider); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancellationBetweenParticipantsOrAtTheFinalBoundaryNeverPublishesCompletion(bool cancelInFinalParticipant)
    {
        await using var health = new HealthcheckService(new());
        using var startupCancellation = new CancellationTokenSource();
        var first = new StartupProbe
        {
            Start = _ =>
            {
                if (!cancelInFinalParticipant) { startupCancellation.Cancel(); }
                return Task.CompletedTask;
            }
        };
        var second = new StartupProbe
        {
            Start = _ =>
            {
                startupCancellation.Cancel();
                return Task.CompletedTask;
            }
        };
        int resolutions = 0;
        var services = new ServiceCollection();
        services.AddSingleton<IStartable>(_ => { resolutions++; return first; });
        services.AddSingleton<IStartable>(_ => { resolutions++; return second; });
        ServiceProvider provider = StartupTestSupport.BuildProvider(services);
        var coordinator = new StartupCoordinator(provider, services.ToArray(), health);
        try
        {
            Task operation = coordinator.StartAsync(startupCancellation.Token);
            Assert.IsInstanceOfType<OperationCanceledException>(await StartupTestSupport.ObserveFailureAsync(operation));
            Assert.IsTrue(operation.IsCanceled);
            Assert.AreEqual(2, resolutions);
            Assert.AreEqual(1, first.Calls);
            int expectedSecondCalls = cancelInFinalParticipant ? 1 : 0;
            Assert.AreEqual(expectedSecondCalls, second.Calls);
            Assert.IsFalse(health.CaptureSnapshot().Application.StartupCompleted);
            Assert.IsFalse(health.CaptureSnapshot().Ready);
            Assert.AreEqual(0, health.CaptureSnapshot().MonitorCounts.CriticalFailing);
            Assert.AreSame(operation, coordinator.StartAsync(CancellationToken.None));
            Assert.AreEqual(2, resolutions);
        }
        finally { await StartupTestSupport.StopAndDisposeAsync(coordinator, provider); }
    }

    [TestMethod]
    public async Task StoppingFromTheFinalParticipantCannotPublishStartupCompletion()
    {
        await using var health = new HealthcheckService(new());
        StartupCoordinator? coordinator = null;
        var participant = new StartupProbe
        {
            Start = _ => { coordinator!.BeginStopping(); return Task.CompletedTask; }
        };
        var services = new ServiceCollection();
        services.AddSingleton<IStartable>(participant);
        ServiceProvider provider = StartupTestSupport.BuildProvider(services);
        coordinator = new StartupCoordinator(provider, services.ToArray(), health);
        try
        {
            Task operation = coordinator.StartAsync(CancellationToken.None);
            Assert.IsInstanceOfType<OperationCanceledException>(await StartupTestSupport.ObserveFailureAsync(operation));
            Assert.IsTrue(operation.IsCanceled);
            Assert.AreEqual(1, participant.Calls);
            Assert.IsTrue(health.CaptureSnapshot().Application.IsStopping);
            Assert.IsFalse(health.CaptureSnapshot().Application.StartupCompleted);
            Assert.IsFalse(health.CaptureSnapshot().Ready);
            Assert.AreEqual(0, health.CaptureSnapshot().MonitorCounts.CriticalFailing);
            Assert.AreSame(operation, coordinator.StartAsync(CancellationToken.None));
        }
        finally { await StartupTestSupport.StopAndDisposeAsync(coordinator, provider); }
    }

    [TestMethod]
    [DataRow("startup", false)]
    [DataRow("startup", true)]
    [DataRow("application-stopping", false)]
    [DataRow("application-stopping", true)]
    [DataRow("owner-stop", false)]
    [DataRow("owner-stop", true)]
    public async Task StopJoinsActualParticipantSettlementBeforeItsRootDependencyCanBeDisposed(
        string cancellationSource, bool acknowledgeCancellation)
    {
        await using var health = new HealthcheckService(new());
        using var startupCancellation = new CancellationTokenSource();
        using var applicationStopping = new CancellationTokenSource();
        var entered = StartupTestSupport.Signal();
        var release = StartupTestSupport.Signal();
        var dependency = new OwnedDependency();
        CancellationToken participantToken = default;
        bool dependencyWasDisposedWhileActive = false;
        var participant = new StartupProbe();
        var services = new ServiceCollection();
        services.AddSingleton(_ => dependency);
        services.AddSingleton<IStartable>(root =>
        {
            OwnedDependency actualDependency = root.GetRequiredService<OwnedDependency>();
            participant.Start = async token =>
            {
                participantToken = token;
                dependencyWasDisposedWhileActive |= actualDependency.Disposals != 0;
                entered.TrySetResult();
                await release.Task;
                dependencyWasDisposedWhileActive |= actualDependency.Disposals != 0;
                if (acknowledgeCancellation) { token.ThrowIfCancellationRequested(); }
            };
            return participant;
        });
        ServiceProvider provider = StartupTestSupport.BuildProvider(services);
        var coordinator = new StartupCoordinator(provider, services.ToArray(), health);
        try
        {
            Task operation = coordinator.StartAsync(startupCancellation.Token, applicationStopping.Token);
            await entered.Task.WaitAsync(StartupTestSupport.Deadline);
            switch (cancellationSource)
            {
                case "startup": startupCancellation.Cancel(); break;
                case "application-stopping": applicationStopping.Cancel(); break;
                case "owner-stop": coordinator.BeginStopping(); break;
                default: throw new AssertFailedException("Unknown controlled cancellation source.");
            }
            Assert.IsTrue(participantToken.IsCancellationRequested);
            Assert.IsFalse(operation.IsCompleted);
            Task stop = coordinator.StopAsync();
            Assert.IsFalse(stop.IsCompleted);
            Assert.AreEqual(0, dependency.Disposals);
            Assert.IsFalse(health.CaptureSnapshot().Application.StartupCompleted);
            release.TrySetResult();
            Assert.IsInstanceOfType<OperationCanceledException>(await StartupTestSupport.ObserveFailureAsync(operation));
            Assert.IsTrue(operation.IsCanceled);
            await stop.WaitAsync(StartupTestSupport.Deadline);
            Assert.AreEqual(0, dependency.Disposals);
            Assert.IsFalse(dependencyWasDisposedWhileActive);
            Assert.AreEqual(0, health.CaptureSnapshot().MonitorCounts.CriticalFailing);
            Assert.AreSame(operation, coordinator.StartAsync(CancellationToken.None));
            Assert.AreEqual(1, participant.Calls);
        }
        finally
        {
            release.TrySetResult();
            await StartupTestSupport.StopAndDisposeAsync(coordinator, provider);
        }
        Assert.AreEqual(1, dependency.Disposals);
    }

    [TestMethod]
    public async Task StoppingBeforeTheFirstEntryClosesEnrollmentWithoutStartingAnOperationAgain()
    {
        await using var health = new HealthcheckService(new());
        int resolutions = 0;
        var participant = new StartupProbe();
        var services = new ServiceCollection();
        services.AddSingleton<IStartable>(_ => { resolutions++; return participant; });
        ServiceProvider provider = StartupTestSupport.BuildProvider(services);
        var coordinator = new StartupCoordinator(provider, services.ToArray(), health);
        try
        {
            coordinator.BeginStopping();
            Task operation = coordinator.StartAsync(CancellationToken.None);
            Assert.IsInstanceOfType<OperationCanceledException>(await StartupTestSupport.ObserveFailureAsync(operation));
            Assert.IsTrue(operation.IsCanceled);
            Assert.AreSame(operation, coordinator.StartAsync(CancellationToken.None));
            Assert.AreEqual(0, resolutions);
            Assert.AreEqual(0, participant.Calls);
            Assert.IsTrue(health.CaptureSnapshot().Application.IsStopping);
            Assert.IsFalse(health.CaptureSnapshot().Application.StartupCompleted);
            Assert.AreEqual(0, health.CaptureSnapshot().MonitorCounts.Critical);
        }
        finally { await StartupTestSupport.StopAndDisposeAsync(coordinator, provider); }
    }

    private sealed class OwnedDependency : IAsyncDisposable
    {
        private int disposals;
        internal int Disposals => Volatile.Read(ref disposals);
        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref disposals);
            return ValueTask.CompletedTask;
        }
    }
}

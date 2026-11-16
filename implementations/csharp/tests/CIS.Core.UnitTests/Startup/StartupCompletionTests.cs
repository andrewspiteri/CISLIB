using CIS.Healthcheck;
using CIS.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Startup;

[TestClass]
public sealed class StartupCompletionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnconfirmedOrFaultedCompletionRetainsOneFailedOperationWithoutPublishingReadiness(bool throws)
    {
        await using var health = new HealthcheckService(new());
        var services = new ServiceCollection();
        var participant = new StartupProbe();
        services.AddSingleton<IStartable>(participant);
        ServiceProvider provider = StartupTestSupport.BuildProvider(services);
        var expected = new InvalidOperationException("Completion failed.");
        int confirmations = 0;
        var coordinator = new StartupCoordinator(provider, services.ToArray(), health,
            confirmStartupCompletion: _ => { confirmations++; return throws ? throw expected : false; });
        try
        {
            Task operation = coordinator.StartAsync(CancellationToken.None);
            Exception failure = await StartupTestSupport.ObserveFailureAsync(operation);
            Assert.IsTrue(operation.IsFaulted);
            if (throws) { Assert.AreSame(expected, failure); }
            else { Assert.IsInstanceOfType<InvalidOperationException>(failure); }
            Assert.IsFalse(health.CaptureSnapshot().Application.StartupCompleted);
            Assert.AreEqual(1, health.CaptureSnapshot().MonitorCounts.CriticalFailing);
            Task repeated = coordinator.StartAsync(new CancellationToken(canceled: true));
            Assert.AreSame(operation, repeated);
            Assert.AreSame(failure, await StartupTestSupport.ObserveFailureAsync(repeated));
            Assert.AreEqual(1, participant.Calls);
            Assert.AreEqual(1, confirmations);
        }
        finally { await StartupTestSupport.StopAndDisposeAsync(coordinator, provider); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task KnownStoppingDuringCompletionWinsOverAnUnconfirmedResultAndStaysNeutral(bool startupToken)
    {
        using var cancellation = new CancellationTokenSource();
        await using var health = new HealthcheckService(new());
        var services = new ServiceCollection();
        ServiceProvider provider = StartupTestSupport.BuildProvider(services);
        StartupCoordinator? coordinator = null;
        coordinator = new StartupCoordinator(provider, services.ToArray(), health,
            confirmStartupCompletion: _ =>
            {
                if (startupToken) { cancellation.Cancel(); }
                else { coordinator!.RequestStopping(); }
                return false;
            });
        try
        {
            Task operation = coordinator.StartAsync(cancellation.Token);
            Assert.IsInstanceOfType<OperationCanceledException>(await StartupTestSupport.ObserveFailureAsync(operation));
            Assert.IsTrue(operation.IsCanceled);
            Assert.AreSame(operation, coordinator.StartAsync(CancellationToken.None));
            Assert.IsFalse(health.CaptureSnapshot().Application.StartupCompleted);
            Assert.AreEqual(0, health.CaptureSnapshot().MonitorCounts.CriticalFailing);
        }
        finally { await StartupTestSupport.StopAndDisposeAsync(coordinator, provider); }
    }
}

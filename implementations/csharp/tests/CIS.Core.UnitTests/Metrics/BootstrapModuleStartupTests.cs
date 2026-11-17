using System.Diagnostics.Metrics;
using CIS.Healthcheck;
using CIS.Metrics;
using CIS.Startup;
using CIS.Tests.UnitTests.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Metrics;

[TestClass]
public sealed class BootstrapModuleStartupTests
{
    [TestMethod]
    public async Task ActualSequentialStartupMeasuresSettledStagesAndPublishesOnlyTheRealCompletionState()
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var metrics = new MetricsRuntime(meter);
        await using var health = new HealthcheckService(new());
        var entered = MetricsTestSupport.Signal();
        var release = MetricsTestSupport.Signal();
        var services = new ServiceCollection();
        var participant = new StartupProbe { Start = async _ => { entered.TrySetResult(); await release.Task; } };
        services.AddSingleton<IStartable>(_ => participant);
        await using ServiceProvider provider = StartupTestSupport.BuildProvider(services);
        await using var coordinator = new StartupCoordinator(provider, services.ToArray(), health);
        coordinator.AttachMetrics(metrics);
        coordinator.AttachMetrics(metrics);
        try
        {
            Task startup = coordinator.StartAsync(CancellationToken.None);
            await entered.Task.WaitAsync(MetricsTestSupport.Deadline);
            listener.Collect();
            Assert.AreEqual(0d, listener.Points("cis.startup.completed").Single().Value);
            Assert.IsFalse(health.CaptureSnapshot().Ready);
            Assert.AreEqual(1, listener.Points("cis.startup.operation.duration").Length);
            release.TrySetResult();
            await startup.WaitAsync(MetricsTestSupport.Deadline);
            MetricsPoint[] durations = listener.Points("cis.startup.operation.duration");
            CollectionAssert.AreEqual(new[] { "construction", "initialization", "completion" },
                durations.Select(point => (string)point.Tags.Single(tag => tag.Key == "stage").Value!).ToArray());
            Assert.IsTrue(durations.All(point => (double)point.Value >= 0));
            Assert.IsTrue(durations.All(point => point.Tags.Single(tag => tag.Key == "outcome").Value!.Equals("success")));
            Assert.IsTrue(health.CaptureSnapshot().Ready);
            Assert.AreSame(startup, coordinator.StartAsync(new CancellationToken(canceled: true)));
            Assert.AreEqual(3, listener.Points("cis.startup.operation.duration").Length);
            listener.Collect();
            Assert.AreEqual(1d, listener.Points("cis.startup.completed").Last().Value);
        }
        finally { release.TrySetResult(); await coordinator.StopAsync().WaitAsync(MetricsTestSupport.Deadline); }
    }

    [TestMethod]
    [DataRow("construction")]
    [DataRow("initialization")]
    [DataRow("completion")]
    public async Task StageFailureKeepsItsOriginalExceptionAndNeverPublishesSuccessfulCompletion(string stage)
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var metrics = new MetricsRuntime(meter);
        var original = new InvalidOperationException("PRIVATE participant detail");
        var services = new ServiceCollection();
        var participant = new StartupProbe { Start = _ => stage == "initialization" ? Task.FromException(original) : Task.CompletedTask };
        services.AddSingleton<IStartable>(_ => stage == "construction" ? throw original : participant);
        await using ServiceProvider provider = StartupTestSupport.BuildProvider(services);
        await using var coordinator = new StartupCoordinator(provider, services.ToArray(),
            confirmStartupCompletion: _ => stage == "completion" ? throw original : true);
        coordinator.AttachMetrics(metrics);
        try
        {
            Exception observed = await StartupTestSupport.ObserveFailureAsync(coordinator.StartAsync(CancellationToken.None));
            Assert.AreSame(original, observed);
            MetricsPoint failed = listener.Points("cis.startup.operation.duration").Single(point =>
                point.Tags.Single(tag => tag.Key == "outcome").Value!.Equals("failed"));
            MetricsTestSupport.AssertTags(failed, new("stage", stage), new("outcome", "failed"));
            Assert.AreEqual(stage == "construction" ? 0 : 1, participant.Calls);
            listener.Collect();
            Assert.AreEqual(0d, listener.Points("cis.startup.completed").Single().Value);
        }
        finally { await coordinator.StopAsync().WaitAsync(MetricsTestSupport.Deadline); }
    }

    [TestMethod]
    public async Task AttachmentDuringAnActualOperationDoesNotReplayItsStagesButCanObserveItsTerminalState()
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var metrics = new MetricsRuntime(meter);
        var entered = MetricsTestSupport.Signal();
        var release = MetricsTestSupport.Signal();
        var services = new ServiceCollection();
        services.AddSingleton<IStartable>(_ => new StartupProbe { Start = async _ => { entered.TrySetResult(); await release.Task; } });
        await using ServiceProvider provider = StartupTestSupport.BuildProvider(services);
        await using var coordinator = new StartupCoordinator(provider, services.ToArray());
        try
        {
            Task startup = coordinator.StartAsync(CancellationToken.None);
            await entered.Task.WaitAsync(MetricsTestSupport.Deadline);
            coordinator.AttachMetrics(metrics);
            release.TrySetResult();
            await startup.WaitAsync(MetricsTestSupport.Deadline);
            Assert.IsEmpty(listener.Points("cis.startup.operation.duration"));
            listener.Collect();
            Assert.AreEqual(1d, listener.Points("cis.startup.completed").Single().Value);
        }
        finally { release.TrySetResult(); await coordinator.StopAsync().WaitAsync(MetricsTestSupport.Deadline); }
    }

    [TestMethod]
    public async Task KnownStartupCancellationRemainsNeutralAndTheLiveOwnerRejectsASecondHost()
    {
        using var meter = new Meter("CIS.Metrics");
        using var otherMeter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var metrics = new MetricsRuntime(meter);
        await using var other = new MetricsRuntime(otherMeter);
        await using var health = new HealthcheckService(new());
        var entered = MetricsTestSupport.Signal();
        using var cancellation = new CancellationTokenSource();
        var services = new ServiceCollection();
        services.AddSingleton<IStartable>(_ => new StartupProbe { Start = async token =>
        { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); } });
        await using ServiceProvider provider = StartupTestSupport.BuildProvider(services);
        await using var coordinator = new StartupCoordinator(provider, services.ToArray(), health);
        coordinator.AttachMetrics(metrics);
        Assert.ThrowsExactly<InvalidOperationException>(() => coordinator.AttachMetrics(other));
        try
        {
            Task startup = coordinator.StartAsync(cancellation.Token);
            await entered.Task.WaitAsync(MetricsTestSupport.Deadline);
            cancellation.Cancel();
            Assert.IsInstanceOfType<OperationCanceledException>(await StartupTestSupport.ObserveFailureAsync(startup));
            MetricsPoint canceled = listener.Points("cis.startup.operation.duration").Last();
            MetricsTestSupport.AssertTags(canceled, new("stage", "initialization"), new("outcome", "cancelled"));
            Assert.AreEqual(0, health.CaptureSnapshot().MonitorCounts.CriticalFailing);
        }
        finally { cancellation.Cancel(); await coordinator.StopAsync().WaitAsync(MetricsTestSupport.Deadline); }
    }
}

using CIS.Healthcheck;
using CIS.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Startup;

[TestClass]
public sealed class StartupCoordinatorExecutionTests
{
    [TestMethod]
    public async Task AHeldActualStartupTaskPreventsTheNextInvocationAndReadinessUntilItCompletes()
    {
        await using var health = new HealthcheckService(new());
        var entered = StartupTestSupport.Signal();
        var release = StartupTestSupport.Signal();
        var order = new List<string>();
        var first = new StartupProbe
        {
            Start = async _ =>
            {
                order.Add("A-entered");
                entered.TrySetResult();
                await release.Task;
                order.Add("A-completed");
            }
        };
        var second = new StartupProbe { Start = _ => { order.Add("B-entered"); return Task.CompletedTask; } };
        var services = new ServiceCollection();
        services.AddSingleton<IStartable>(first);
        services.AddSingleton<IStartable>(second);
        ServiceProvider provider = StartupTestSupport.BuildProvider(services);
        var coordinator = new StartupCoordinator(provider, services.ToArray(), health);
        try
        {
            Task operation = coordinator.StartAsync(CancellationToken.None);
            await entered.Task.WaitAsync(StartupTestSupport.Deadline);
            Assert.IsFalse(operation.IsCompleted);
            Assert.AreEqual(1, first.Calls);
            Assert.AreEqual(0, second.Calls);
            Assert.IsFalse(health.CaptureSnapshot().Ready);
            release.TrySetResult();
            await operation.WaitAsync(StartupTestSupport.Deadline);
            CollectionAssert.AreEqual(new[] { "A-entered", "A-completed", "B-entered" }, order);
            Assert.AreEqual(1, second.Calls);
            Assert.IsTrue(health.CaptureSnapshot().Ready);
        }
        finally
        {
            release.TrySetResult();
            await StartupTestSupport.StopAndDisposeAsync(coordinator, provider);
        }
    }

    [TestMethod]
    public async Task ConcurrentAndRepeatedEntryRetainTheFirstOperationAndIgnoreLaterCancellationTokens()
    {
        await using var health = new HealthcheckService(new());
        using var firstStartup = new CancellationTokenSource();
        using var firstStopping = new CancellationTokenSource();
        using var laterStartup = new CancellationTokenSource();
        using var laterStopping = new CancellationTokenSource();
        var entered = StartupTestSupport.Signal();
        var release = StartupTestSupport.Signal();
        int resolutions = 0;
        var participant = new StartupProbe { Start = _ => { entered.TrySetResult(); return release.Task; } };
        var services = new ServiceCollection();
        services.AddSingleton<IStartable>(_ => { resolutions++; return participant; });
        ServiceProvider provider = StartupTestSupport.BuildProvider(services);
        var coordinator = new StartupCoordinator(provider, services.ToArray(), health);
        try
        {
            Task first = coordinator.StartAsync(firstStartup.Token, firstStopping.Token);
            await entered.Task.WaitAsync(StartupTestSupport.Deadline);
            Task second = coordinator.StartAsync(laterStartup.Token, laterStopping.Token);
            Assert.AreSame(first, second);
            laterStartup.Cancel();
            laterStopping.Cancel();
            Assert.IsFalse(first.IsCompleted);
            Assert.IsFalse(participant.Tokens.Single().IsCancellationRequested);
            Assert.IsTrue(participant.Tokens.Single().CanBeCanceled);
            Assert.AreEqual(1, resolutions);
            Assert.AreEqual(1, participant.Calls);
            release.TrySetResult();
            await first.WaitAsync(StartupTestSupport.Deadline);
            Task repeated = coordinator.StartAsync(new CancellationToken(canceled: true), new CancellationToken(canceled: true));
            Assert.AreSame(first, repeated);
            await repeated.WaitAsync(StartupTestSupport.Deadline);
            Assert.AreEqual(1, resolutions);
            Assert.AreEqual(1, participant.Calls);
            Assert.IsTrue(health.CaptureSnapshot().Ready);
        }
        finally
        {
            release.TrySetResult();
            await StartupTestSupport.StopAndDisposeAsync(coordinator, provider);
        }
    }

    [TestMethod]
    [DataRow("synchronous", true)]
    [DataRow("synchronous", false)]
    [DataRow("asynchronous", true)]
    [DataRow("asynchronous", false)]
    [DataRow("null-task", true)]
    [DataRow("null-task", false)]
    [DataRow("unexpected-cancellation", true)]
    [DataRow("unexpected-cancellation", false)]
    public async Task EscapingStartupFailuresStopTheSequenceAndRemainFailedWithoutReplay(string mode, bool withHealth)
    {
        await using HealthcheckService? health = withHealth ? new HealthcheckService(new()) : null;
        var entered = StartupTestSupport.Signal();
        var release = StartupTestSupport.Signal();
        Exception expected = mode == "unexpected-cancellation"
            ? new OperationCanceledException("No host cancellation was requested.")
            : new InvalidOperationException("The original participant failure.");
        var first = new StartupProbe
        {
            Start = _ =>
            {
                entered.TrySetResult();
                return mode switch
                {
                    "synchronous" => throw expected,
                    "asynchronous" => FailAfterReleaseAsync(release.Task, expected),
                    "null-task" => null!,
                    "unexpected-cancellation" => Task.FromException(expected),
                    _ => throw new ArgumentOutOfRangeException(nameof(mode))
                };
            }
        };
        var later = new StartupProbe();
        int resolutions = 0;
        var services = new ServiceCollection();
        services.AddSingleton<IStartable>(_ => { resolutions++; return first; });
        services.AddSingleton<IStartable>(_ => { resolutions++; return later; });
        ServiceProvider provider = StartupTestSupport.BuildProvider(services);
        var coordinator = new StartupCoordinator(provider, services.ToArray(), health);
        try
        {
            Task operation = coordinator.StartAsync(CancellationToken.None);
            await entered.Task.WaitAsync(StartupTestSupport.Deadline);
            if (mode == "asynchronous")
            {
                Assert.IsFalse(operation.IsCompleted);
                Assert.AreEqual(0, later.Calls);
            }
            release.TrySetResult();
            Exception actual = await StartupTestSupport.ObserveFailureAsync(operation);
            if (mode == "null-task") { Assert.IsInstanceOfType<InvalidOperationException>(actual); }
            else { Assert.AreSame(expected, actual); }
            Assert.IsTrue(operation.IsFaulted, "Unexplained cancellation is a startup failure, not neutral host cancellation.");
            Assert.AreEqual(1, first.Calls);
            Assert.AreEqual(0, later.Calls);
            Assert.AreEqual(2, resolutions);
            if (health is not null)
            {
                HealthSnapshot snapshot = health.CaptureSnapshot();
                Assert.IsFalse(snapshot.Application.StartupCompleted);
                Assert.IsFalse(snapshot.Ready);
                Assert.AreEqual(1, snapshot.MonitorCounts.CriticalFailing);
                Assert.AreEqual("Startup.Failed", snapshot.Monitors.Single().Diagnostic!.Code);
            }
            Task repeated = coordinator.StartAsync(new CancellationToken(canceled: true));
            Assert.AreSame(operation, repeated);
            Assert.AreSame(await StartupTestSupport.ObserveFailureAsync(repeated), actual);
            Assert.AreEqual(1, first.Calls);
            Assert.AreEqual(2, resolutions);
        }
        finally
        {
            release.TrySetResult();
            await StartupTestSupport.StopAndDisposeAsync(coordinator, provider);
        }
    }

    [TestMethod]
    public async Task TheRetainedOperationIsPublishedBeforeAnyUserCodeCanReenterStartup()
    {
        var services = new ServiceCollection();
        var participant = new StartupProbe();
        StartupCoordinator? coordinator = null;
        Task? reentrant = null;
        participant.Start = _ =>
        {
            if (participant.Calls != 1) { throw new InvalidOperationException("Startup reentered a participant before retaining its operation."); }
            reentrant = coordinator!.StartAsync(new CancellationToken(canceled: true));
            return Task.CompletedTask;
        };
        services.AddSingleton<IStartable>(participant);
        ServiceProvider provider = StartupTestSupport.BuildProvider(services);
        coordinator = new StartupCoordinator(provider, services.ToArray());
        try
        {
            Task operation = coordinator.StartAsync(CancellationToken.None);
            await operation.WaitAsync(StartupTestSupport.Deadline);
            Assert.AreSame(operation, reentrant);
            Assert.AreEqual(1, participant.Calls);
        }
        finally { await StartupTestSupport.StopAndDisposeAsync(coordinator, provider); }
    }

    [TestMethod]
    public async Task ABlockingSynchronousParticipantPrologueDoesNotHoldTheCoordinatorControlGate()
    {
        var entered = StartupTestSupport.Signal();
        var release = StartupTestSupport.Signal();
        var participant = new StartupProbe
        {
            Start = _ =>
            {
                entered.TrySetResult();
                release.Task.WaitAsync(StartupTestSupport.Deadline).GetAwaiter().GetResult();
                return Task.CompletedTask;
            }
        };
        var services = new ServiceCollection();
        services.AddSingleton<IStartable>(participant);
        ServiceProvider provider = StartupTestSupport.BuildProvider(services);
        var coordinator = new StartupCoordinator(provider, services.ToArray());
        Task<Task>? firstEntry = null;
        try
        {
            firstEntry = Task.Factory.StartNew(() => coordinator.StartAsync(CancellationToken.None),
                CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
            await entered.Task.WaitAsync(StartupTestSupport.Deadline);
            Task<Task> secondEntry = Task.Factory.StartNew(() => coordinator.StartAsync(CancellationToken.None),
                CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
            Task joined = await secondEntry.WaitAsync(StartupTestSupport.Deadline);
            Assert.IsFalse(joined.IsCompleted);
            Assert.AreEqual(1, participant.Calls);
            release.TrySetResult();
            Task original = await firstEntry.WaitAsync(StartupTestSupport.Deadline);
            Assert.AreSame(original, joined);
            await original.WaitAsync(StartupTestSupport.Deadline);
        }
        finally
        {
            release.TrySetResult();
            try { if (firstEntry is not null) { await firstEntry.WaitAsync(StartupTestSupport.Deadline); } }
            finally { await StartupTestSupport.StopAndDisposeAsync(coordinator, provider); }
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ExistingHealthPolicyAffectsReadinessWithoutBlockingOrReplayingSuccessfulStartup(bool critical)
    {
        await using var health = new HealthcheckService(new());
        await using IConditionMonitor applicationCondition = health.CreateCondition(new() { IsCritical = critical, InitiallyFlagged = true });
        var participant = new StartupProbe();
        var services = new ServiceCollection();
        services.AddSingleton<IStartable>(participant);
        ServiceProvider provider = StartupTestSupport.BuildProvider(services);
        var coordinator = new StartupCoordinator(provider, services.ToArray(), health);
        try
        {
            Task operation = coordinator.StartAsync(CancellationToken.None);
            await operation.WaitAsync(StartupTestSupport.Deadline);
            HealthSnapshot initial = health.CaptureSnapshot();
            Assert.IsTrue(initial.Application.StartupCompleted);
            bool expectedReady = !critical;
            Assert.AreEqual(expectedReady, initial.Ready);
            applicationCondition.Clear();
            Assert.IsTrue(health.CaptureSnapshot().Ready);
            Assert.AreSame(operation, coordinator.StartAsync(CancellationToken.None));
            Assert.AreEqual(1, participant.Calls);
        }
        finally { await StartupTestSupport.StopAndDisposeAsync(coordinator, provider); }
    }

    private static async Task FailAfterReleaseAsync(Task release, Exception error)
    {
        await release;
        throw error;
    }
}

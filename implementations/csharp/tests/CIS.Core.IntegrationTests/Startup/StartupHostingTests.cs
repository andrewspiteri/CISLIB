using System.Net;
using CIS.Startup;
using CIS.Startup.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Startup;

[TestClass]
public sealed class StartupHostingTests
{
    [TestMethod]
    public async Task StartupAloneUsesTheActualRootWithoutRequiringOptionalHealthOrHeartbeatModules()
    {
        var participant = new StartupParticipant();
        IHost host = Host.CreateDefaultBuilder()
            .UseDefaultServiceProvider(options => { options.ValidateScopes = true; options.ValidateOnBuild = true; })
            .ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureServices(services =>
            { services.AddSingleton<IStartable>(participant); services.AddCisStartup(); })
            .Build();
        try
        {
            await host.StartAsync().WaitAsync(StartupHost.Deadline);
            Assert.AreSame(participant, host.Services.GetRequiredService<IStartable>());
            Assert.AreEqual(1, participant.Calls);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).WaitAsync(StartupHost.Deadline);
            if (host is IAsyncDisposable asynchronous) { await asynchronous.DisposeAsync().AsTask().WaitAsync(StartupHost.Deadline); }
            else { host.Dispose(); }
        }
    }

    [TestMethod]
    public async Task HostStoppingJoinsAnIgnoringStartupTaskBeforeDependentLifecycleStoppingAndRootDisposal()
    {
        var dependency = new StartupDependency();
        var entered = StartupHost.Signal();
        var release = StartupHost.Signal();
        var cancellationObserved = StartupHost.Signal();
        bool dependencyStayedLive = false;
        var participant = new StartupParticipant(async token =>
        {
            using CancellationTokenRegistration registration = token.Register(() => cancellationObserved.TrySetResult());
            entered.TrySetResult();
            await release.Task;
            dependencyStayedLive = !dependency.Stopping.Task.IsCompleted && dependency.Disposals == 0;
        });
        var host = new StartupHost(services =>
        { services.AddSingleton<IHostedService>(_ => dependency); services.AddSingleton<IStartable>(participant); });
        await using (host)
        {
            host.Release.Add(() => release.TrySetResult());
            var subscriber = new StartupSubscriber();
            host.Owner.Service.Register(subscriber, StartupHost.Schedule);
            Task start = host.StartAsync();
            await entered.Task.WaitAsync(StartupHost.Deadline);

            Task stop = host.StopAsync(new CancellationToken(canceled: true));
            await cancellationObserved.Task.WaitAsync(StartupHost.Deadline);
            Assert.IsFalse(stop.IsCompleted, "A canceled host budget cannot release a participant's live dependency.");
            Assert.IsFalse(start.IsCompleted);
            Assert.IsFalse(dependency.Stopping.Task.IsCompleted);
            Assert.AreEqual(0, dependency.Disposals);
            Assert.IsTrue(host.Health.CaptureSnapshot().Application.IsStopping);
            await host.AssertStatusAsync("/_health/ready", HttpStatusCode.InternalServerError);
            await host.AssertStatusAsync("/countries", HttpStatusCode.ServiceUnavailable);
            Assert.AreEqual(0, host.BusinessCalls);
            release.TrySetResult();
            await ObserveCancellationAsync(start);
            Assert.IsTrue(start.IsCanceled);
            await ObserveCancellationAsync(stop);
            await dependency.Stopping.Task.WaitAsync(StartupHost.Deadline);
            Assert.IsTrue(dependencyStayedLive);
            Assert.IsFalse(host.Health.CaptureSnapshot().Application.StartupCompleted);
            Assert.AreEqual(0, subscriber.Calls);
            Assert.AreEqual(0, dependency.Disposals);
        }
        Assert.AreEqual(1, dependency.Disposals, "Only the real root's later disposal owns this dependency.");
    }

    [TestMethod]
    public async Task HostStoppingRetainsDependenciesThroughHeartbeatCallbackAndItsAwaitedRecovery()
    {
        var dependency = new StartupDependency();
        var callbackRelease = StartupHost.Signal();
        var handlerEntered = StartupHost.Signal();
        var handlerRelease = StartupHost.Signal();
        bool callbackHadLiveDependency = false, handlerHadLiveDependency = false;
        var subscriber = new StartupSubscriber(async _ =>
        {
            await callbackRelease.Task;
            callbackHadLiveDependency = !dependency.Stopping.Task.IsCompleted && dependency.Disposals == 0;
            throw new InvalidOperationException("Controlled refresh failure.");
        }, async _ =>
        {
            handlerEntered.TrySetResult();
            await handlerRelease.Task;
            handlerHadLiveDependency = !dependency.Stopping.Task.IsCompleted && dependency.Disposals == 0;
        });
        var participant = new StartupParticipant();
        var host = new StartupHost(services =>
        { services.AddSingleton<IHostedService>(_ => dependency); services.AddSingleton<IStartable>(participant); });
        await using (host)
        {
            host.Release.Add(() => callbackRelease.TrySetResult());
            host.Release.Add(() => handlerRelease.TrySetResult());
            host.Owner.Service.Register(subscriber, StartupHost.Schedule);
            await host.StartAsync().WaitAsync(StartupHost.Deadline);
            await subscriber.Entered.Task.WaitAsync(StartupHost.Deadline);
            Task stop = host.StopAsync(new CancellationToken(canceled: true));
            Assert.IsFalse(stop.IsCompleted);
            Assert.IsFalse(dependency.Stopping.Task.IsCompleted);
            Assert.IsFalse(host.Health.CaptureSnapshot().Ready);
            callbackRelease.TrySetResult();
            await handlerEntered.Task.WaitAsync(StartupHost.Deadline);
            Assert.IsFalse(stop.IsCompleted, "The real Heartbeat barrier includes asynchronous recovery.");
            Assert.IsFalse(dependency.Stopping.Task.IsCompleted);
            Assert.AreEqual(0, dependency.Disposals);
            handlerRelease.TrySetResult();
            await ObserveCancellationAsync(stop);
            await dependency.Stopping.Task.WaitAsync(StartupHost.Deadline);
            Assert.IsTrue(callbackHadLiveDependency);
            Assert.IsTrue(handlerHadLiveDependency);
            Assert.AreEqual(1, participant.Calls);
            Assert.AreEqual(1, subscriber.Calls);
            Assert.AreEqual(1, subscriber.Errors);
            Assert.IsFalse(host.Runtime.InfrastructureFailure.IsCompleted);
        }
        Assert.AreEqual(1, dependency.Disposals);
    }

    [TestMethod]
    [DataRow("start-concurrently")]
    [DataRow("stop-concurrently")]
    [DataRow("lifecycle-type-tail")]
    [DataRow("lifecycle-factory-tail")]
    [DataRow("ordinary-factory-tail")]
    [DataRow("heartbeat-after-startup")]
    public async Task UnsupportedCompositionFailsClosedWithoutStartupCallbacksOrASecondHostedServiceResolution(string mode)
    {
        int factoryCalls = 0;
        var participant = new StartupParticipant();
        await using var host = new StartupHost(services =>
        {
            services.AddSingleton<IStartable>(participant);
            services.Configure<HostOptions>(options =>
            {
                options.ServicesStartConcurrently = mode == "start-concurrently";
                options.ServicesStopConcurrently = mode == "stop-concurrently";
            });
        }, services =>
        {
            switch (mode)
            {
                case "lifecycle-type-tail": services.AddSingleton<IHostedService, LifecycleTail>(); break;
                case "lifecycle-factory-tail":
                    services.AddSingleton<IHostedService>(_ => { Interlocked.Increment(ref factoryCalls); return new LifecycleTail(); }); break;
                case "ordinary-factory-tail":
                    services.AddSingleton<IHostedService>(_ => { Interlocked.Increment(ref factoryCalls); return new OrdinaryTail(); }); break;
                case "heartbeat-after-startup":
                    ServiceDescriptor heartbeat = services.Single(item => !item.IsKeyedService && item.ServiceType == typeof(IHostedService)
                        && item.ImplementationType?.FullName == "CIS.Heartbeat.Hosting.HeartbeatHostedService");
                    services.Remove(heartbeat);
                    services.Add(heartbeat);
                    break;
            }
        });
        var subscriber = new StartupSubscriber();
        host.Owner.Service.Register(subscriber, StartupHost.Schedule);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync()).WaitAsync(StartupHost.Deadline);
        Assert.AreEqual(0, participant.Calls);
        Assert.AreEqual(0, subscriber.Calls);
        Assert.IsFalse(host.Health.CaptureSnapshot().Application.StartupCompleted);
        Assert.AreEqual(mode.Contains("factory", StringComparison.Ordinal) ? 1 : 0, Volatile.Read(ref factoryCalls),
            "The host may construct its normal array once; the order guard must never resolve another graph.");
    }

    [TestMethod]
    public async Task KeyedHostedServiceTailIsNotPartOfTheOrdinaryHostGraphAndDoesNotBreakTheSupportedWebComposition()
    {
        int keyedConstructions = 0;
        var participant = new StartupParticipant();
        await using var host = new StartupHost(services => services.AddSingleton<IStartable>(participant), services =>
            services.AddKeyedSingleton<IHostedService>("unused-tail", (_, _) =>
            { Interlocked.Increment(ref keyedConstructions); throw new InvalidOperationException("Keyed-only services do not enroll with Generic Host."); }));
        await host.StartAsync().WaitAsync(StartupHost.Deadline);
        Assert.AreEqual(0, keyedConstructions);
        Assert.AreEqual(1, participant.Calls);
        await host.AssertStatusAsync("/_health/ready", HttpStatusCode.OK);
    }

    private static async Task ObserveCancellationAsync(Task operation)
    {
        try { await operation.WaitAsync(StartupHost.Deadline); }
        catch (OperationCanceledException) when (operation.IsCanceled) { }
    }

    public sealed class OrdinaryTail : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class LifecycleTail : IHostedLifecycleService
    {
        public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

using System.Collections.Concurrent;
using System.Net;
using CIS.Configuration;
using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.Hosting;
using CIS.Logging;
using CIS.RestClient;
using CIS.Startup;
using CIS.TestSupport;
using CIS.Tests.IntegrationTests.Configuration;
using CIS.Tests.IntegrationTests.Startup;
using CIS.Tests.UnitTests.RestClient;
using CIS.TypeManagement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Application;

[TestClass]
public sealed class CISApplicationLifecycleTests
{
    [TestMethod]
    public async Task RealPreparationLoadsBothScopesBeforeBuildAndRetainsTheExactRootsAcrossTheOneProvider()
    {
        using var directory = new TemporaryDirectory();
        TypeManagerOptions inventory = ApplicationTestSupport.Inventory(directory);
        var store = new ConfigurationHostStore();
        string definition = ConfigurationHostTestSupport.Definition<ConfigurationHostSettings>();
        store.Records[new("CommonConfiguration", definition)] = ConfigurationHostStore.Json("{\"Count\":40}", "common");
        store.Records[new(ApplicationTestSupport.HostName, definition)] = ConfigurationHostStore.Json("{\"Count\":42}", "application");
        store.Set<ConfigurationHostDirectSettings>("{\"Enabled\":false}", "direct");
        var entered = ApplicationTestSupport.Signal();
        var release = ApplicationTestSupport.Signal();
        store.Read = async (key, token, _) =>
        {
            if (key.Scope == "CommonConfiguration" && key.Definition == definition)
            { entered.TrySetResult(); await release.Task; }
            return store.Current(key, token);
        };
        int constructions = 0;
        await using var app = new CISApplication(builder =>
        {
            ApplicationTestSupport.Configure(builder, inventory);
            builder.Configuration = new(ApplicationTestSupport.HostName, store) { IsHealthCritical = true };
            builder.Services.AddSingleton(root =>
            {
                Interlocked.Increment(ref constructions);
                return new ConfigurationHostConsumer(root.GetRequiredService<ConfigurationHostSettings>(),
                    root.GetRequiredService<ConfigurationHostDirectSettings>(), root.GetRequiredService<IConfigurationReader>());
            });
            builder.Services.AddSingleton<IStartable>(root => root.GetRequiredService<ConfigurationHostConsumer>());
        });
        Task start = app.StartAsync();
        try
        {
            await entered.Task.WaitAsync(ApplicationTestSupport.Deadline);
            Assert.IsFalse(start.IsCompleted);
            Assert.AreEqual(0, constructions);
            Assert.Throws<InvalidOperationException>(() => _ = app.Services);
            release.TrySetResult();
            await start.WaitAsync(ApplicationTestSupport.Deadline);
            ConfigurationHostConsumer consumer = app.Services.GetRequiredService<ConfigurationHostConsumer>();
            Assert.AreEqual((42, false), consumer.ConstructorValues);
            Assert.AreEqual((42, false), consumer.StartedValues.GetValueOrDefault());
            Assert.AreEqual(1, constructions);
            Assert.AreEqual(1, consumer.Calls);
            Assert.AreEqual("CIS.Core.IntegrationTests", ApplicationTestSupport.HostName);
            ITypeManager manager = app.Services.GetRequiredService<ITypeManager>();
            CollectionAssert.AreEquivalent(new[] { typeof(ConfigurationHostSettings), typeof(ConfigurationHostDirectSettings) },
                manager.GetImplementations<IConfigurationObject>().ToArray());
            using IServiceScope scope = app.Services.CreateScope();
            Assert.AreSame(consumer.Settings, scope.ServiceProvider.GetRequiredService<ConfigurationHostSettings>());
            Assert.AreSame(consumer.Direct, scope.ServiceProvider.GetRequiredService<ConfigurationHostDirectSettings>());
            Assert.AreSame(app.Services.GetRequiredService<IConfigurationReader>(),
                scope.ServiceProvider.GetRequiredService<IConfigurationReader>());
            Assert.IsTrue(store.Requests.All(request => request.Key.Scope is "CommonConfiguration" or "CIS.Core.IntegrationTests"));
        }
        finally
        {
            release.TrySetResult();
            await ApplicationTestSupport.SettleAsync(start);
            await app.StopAsync().WaitAsync(ApplicationTestSupport.Deadline);
        }
        Assert.AreEqual(1, store.WatchDisposals);
        Assert.AreEqual(0, store.Disposals, "The bootstrap store remains caller owned.");
    }

    [TestMethod]
    public async Task AllScheduledRootsAreConstructedBeforeSequentialStartAndReferenceAliasesStartOnlyOnce()
    {
        using var directory = new TemporaryDirectory();
        TypeManagerOptions inventory = ApplicationTestSupport.Inventory(directory);
        var trace = new ConcurrentQueue<string>();
        var entered = ApplicationTestSupport.Signal();
        var release = ApplicationTestSupport.Signal();
        var first = new FirstParticipant(async _ =>
        { trace.Enqueue("start-first"); entered.TrySetResult(); await release.Task; });
        var second = new SecondParticipant(_ => { trace.Enqueue("start-second"); return Task.CompletedTask; });
        await using var app = new CISApplication(builder =>
        {
            ApplicationTestSupport.Configure(builder, inventory);
            builder.Services.AddSingleton(_ => { trace.Enqueue("construct-first"); return first; });
            builder.Services.AddSingleton<IStartable>(root => root.GetRequiredService<FirstParticipant>());
            builder.Services.AddSingleton(_ => { trace.Enqueue("construct-second"); return second; });
            builder.Services.AddSingleton<IStartable>(root => root.GetRequiredService<SecondParticipant>());
            builder.Services.AddSingleton<IStartable>(root => root.GetRequiredService<FirstParticipant>());
        });
        Task start = app.StartAsync();
        try
        {
            await entered.Task.WaitAsync(ApplicationTestSupport.Deadline);
            CollectionAssert.AreEqual(new[] { "construct-first", "construct-second", "start-first" }, trace.ToArray());
            Assert.AreEqual(0, second.Calls);
            release.TrySetResult();
            await start.WaitAsync(ApplicationTestSupport.Deadline);
            CollectionAssert.AreEqual(new[] { "construct-first", "construct-second", "start-first", "start-second" }, trace.ToArray());
            Assert.AreEqual(1, first.Calls);
            Assert.AreEqual(1, second.Calls);
            Assert.IsTrue(app.Services.GetRequiredService<ConfigurationRuntime>().CaptureStatus().ActivationCompleted);
        }
        finally
        {
            release.TrySetResult();
            await ApplicationTestSupport.SettleAsync(start);
            await app.StopAsync().WaitAsync(ApplicationTestSupport.Deadline);
        }
    }

    [TestMethod]
    public async Task WebFactoryMapsOnceAndSharesTheProviderWhileEarlyHealthAndBusinessReadinessStaySeparated()
    {
        using var directory = new TemporaryDirectory();
        TypeManagerOptions inventory = ApplicationTestSupport.Inventory(directory);
        var entered = ApplicationTestSupport.Signal();
        var release = ApplicationTestSupport.Signal();
        int maps = 0, business = 0;
        IServiceProvider? mappedRoot = null;
        var participant = new ApplicationTestSupport.Participant(async _ =>
        { entered.TrySetResult(); await release.Task; });
        await using CISApplication app = CISApplication.CreateWeb([], builder =>
        {
            ApplicationTestSupport.Configure(builder, inventory);
            builder.Services.AddSingleton<IStartable>(participant);
            builder.Services.AddSingleton<RootReference>();
        }, web =>
        {
            Interlocked.Increment(ref maps);
            mappedRoot = web.Services;
            web.MapGet("/business", (HttpContext context) =>
            {
                Assert.AreSame(mappedRoot!.GetRequiredService<RootReference>(),
                    context.RequestServices.GetRequiredService<RootReference>());
                Interlocked.Increment(ref business);
                return "business";
            });
        });
        Task start = app.StartAsync();
        try
        {
            await entered.Task.WaitAsync(ApplicationTestSupport.Deadline);
            Assert.AreSame(mappedRoot, app.Services);
            using HttpClient client = ApplicationTestSupport.Client(app);
            await ApplicationTestSupport.StatusAsync(client, "/_health/ready", HttpStatusCode.InternalServerError);
            await ApplicationTestSupport.StatusAsync(client, "/business", HttpStatusCode.ServiceUnavailable);
            Assert.AreEqual(0, business);
            release.TrySetResult();
            await start.WaitAsync(ApplicationTestSupport.Deadline);
            Assert.AreSame(start, app.StartAsync(new CancellationToken(canceled: true)));
            await ApplicationTestSupport.StatusAsync(client, "/_health/ready", HttpStatusCode.OK);
            await ApplicationTestSupport.StatusAsync(client, "/business", HttpStatusCode.OK);
            Assert.AreEqual(1, maps);
            Assert.AreEqual(1, business);
            Assert.AreEqual(1, participant.Calls);
        }
        finally
        {
            release.TrySetResult();
            await ApplicationTestSupport.SettleAsync(start);
            await app.StopAsync().WaitAsync(ApplicationTestSupport.Deadline);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TheFirstStartupFailureOrCancellationIsRetainedWithoutRepeatedInitializationOrReadiness(bool cancel)
    {
        using var directory = new TemporaryDirectory();
        TypeManagerOptions inventory = ApplicationTestSupport.Inventory(directory);
        using var cancellation = new CancellationTokenSource();
        var entered = ApplicationTestSupport.Signal();
        var release = ApplicationTestSupport.Signal();
        var original = new InvalidOperationException("initializer failure");
        var participant = new ApplicationTestSupport.Participant(async token =>
        {
            entered.TrySetResult();
            await release.Task;
            if (cancel) { token.ThrowIfCancellationRequested(); }
            throw original;
        });
        await using CISApplication app = CISApplication.CreateWeb([], builder =>
        {
            ApplicationTestSupport.Configure(builder, inventory);
            builder.Services.AddSingleton<IStartable>(participant);
        }, web => web.MapGet("/business", () => "business"));
        Task first = app.StartAsync(cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(ApplicationTestSupport.Deadline);
            Assert.AreSame(first, app.StartAsync(new CancellationToken(canceled: true)));
            using HttpClient client = ApplicationTestSupport.Client(app);
            await ApplicationTestSupport.StatusAsync(client, "/_health/ready", HttpStatusCode.InternalServerError);
            if (cancel) { cancellation.Cancel(); }
            Assert.IsFalse(first.IsCompleted);
            release.TrySetResult();
            Exception failure = await ApplicationTestSupport.FailureAsync(first);
            if (cancel)
            {
                Assert.IsInstanceOfType<OperationCanceledException>(failure);
                Assert.IsTrue(first.IsCanceled);
                Assert.IsNull(app.Failure);
            }
            else
            {
                Assert.AreSame(original, failure);
                Assert.AreSame(original, app.Failure);
                Assert.AreEqual(1, await app.RunAsync().WaitAsync(ApplicationTestSupport.Deadline));
            }
            Assert.AreSame(first, app.StartAsync());
            Assert.AreEqual(1, participant.Calls);
            if (cancel) { await ApplicationTestSupport.StatusAsync(client, "/_health/ready", HttpStatusCode.InternalServerError); }
        }
        finally
        {
            release.TrySetResult();
            await ApplicationTestSupport.SettleAsync(first);
            await app.StopAsync().WaitAsync(ApplicationTestSupport.Deadline);
        }
    }

    [TestMethod]
    public async Task AStartupFaultChosenBeforeLateCancellationKeepsItsOriginalForeignExceptionAndFaultedTask()
    {
        using var directory = new TemporaryDirectory();
        TypeManagerOptions inventory = ApplicationTestSupport.Inventory(directory);
        using var cancellation = new CancellationTokenSource();
        using var foreign = new CancellationTokenSource();
        foreign.Cancel();
        var original = new OperationCanceledException("unrelated participant cancellation", foreign.Token);
        var entered = ApplicationTestSupport.Signal();
        var release = ApplicationTestSupport.Signal();
        // The existing diagnostic happens after the coordinator selected failure,
        // so this is a deterministic late-cancellation boundary, not a token race.
        using var observer = new CancelAfterStartupFailure(cancellation);
        var participant = new ApplicationTestSupport.Participant(async _ =>
        { entered.TrySetResult(); await release.Task; throw original; });
        await using var app = new CISApplication(builder =>
        {
            ApplicationTestSupport.Configure(builder, inventory);
            builder.Host.Logging.AddProvider(observer);
            builder.Services.AddSingleton<IStartable>(participant);
        });
        Task start = app.StartAsync(cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(ApplicationTestSupport.Deadline);
            Assert.IsFalse(cancellation.IsCancellationRequested);
            release.TrySetResult();
            await observer.Classified.Task.WaitAsync(ApplicationTestSupport.Deadline);
            Assert.IsTrue(cancellation.IsCancellationRequested);
            Exception actual = await ApplicationTestSupport.FailureAsync(start);
            Assert.AreSame(original, actual);
            Assert.IsTrue(start.IsFaulted, "Late host cancellation cannot turn the retained foreign startup fault into neutral cancellation.");
            Assert.IsFalse(start.IsCanceled);
            Assert.AreSame(original, app.Failure);
            Assert.AreSame(start, app.StartAsync());
            Assert.AreEqual(1, participant.Calls);
            Assert.IsFalse(app.Services.GetRequiredService<HealthcheckService>().CaptureSnapshot().Ready);
        }
        finally
        {
            release.TrySetResult();
            await ApplicationTestSupport.SettleAsync(start);
            await app.StopAsync().WaitAsync(ApplicationTestSupport.Deadline);
        }
    }

    [TestMethod]
    public async Task ACanceledStopWaitCannotDisposeDependenciesWhileAnIgnoringInitializerStillOwnsThem()
    {
        using var directory = new TemporaryDirectory();
        TypeManagerOptions inventory = ApplicationTestSupport.Inventory(directory);
        var dependency = new ApplicationTestSupport.Dependency();
        var entered = ApplicationTestSupport.Signal();
        var release = ApplicationTestSupport.Signal();
        var participant = new ApplicationTestSupport.Participant(async _ =>
        { entered.TrySetResult(); await release.Task; dependency.Use(); });
        var app = new CISApplication(builder =>
        {
            ApplicationTestSupport.Configure(builder, inventory);
            ApplicationTestSupport.RegisterDependency(builder.Services, dependency);
            builder.Services.AddSingleton<IStartable>(participant);
        });
        Task start = app.StartAsync();
        Task? stop = null, disposal = null;
        try
        {
            await entered.Task.WaitAsync(ApplicationTestSupport.Deadline);
            Task canceledWait = app.StopAsync(new CancellationToken(canceled: true));
            stop = app.StopAsync();
            Assert.AreSame(stop, app.StopAsync());
            await Assert.ThrowsAsync<OperationCanceledException>(() => canceledWait);
            disposal = app.DisposeAsync().AsTask();
            Assert.AreSame(disposal, app.DisposeAsync().AsTask());
            Assert.IsFalse(start.IsCompleted);
            Assert.IsFalse(stop.IsCompleted);
            Assert.IsFalse(disposal.IsCompleted);
            Assert.IsFalse(dependency.Stopping.Task.IsCompleted);
            Assert.AreEqual(0, dependency.Disposals);
            release.TrySetResult();
            Assert.IsInstanceOfType<OperationCanceledException>(await ApplicationTestSupport.FailureAsync(start));
            await stop.WaitAsync(ApplicationTestSupport.Deadline);
            await disposal.WaitAsync(ApplicationTestSupport.Deadline);
            Assert.IsTrue(dependency.Stopping.Task.IsCompleted);
            Assert.AreEqual(1, dependency.Disposals);
            Assert.AreSame(start, app.StartAsync());
        }
        finally
        {
            release.TrySetResult();
            await ApplicationTestSupport.SettleAsync(start);
            await (stop ?? app.StopAsync()).WaitAsync(ApplicationTestSupport.Deadline);
            await (disposal ?? app.DisposeAsync().AsTask()).WaitAsync(ApplicationTestSupport.Deadline);
        }
    }

    [TestMethod]
    public async Task ARealBootstrapFailureDoesNotBuildConstructConsumersOrSubstituteDefaults()
    {
        using var directory = new TemporaryDirectory();
        TypeManagerOptions inventory = ApplicationTestSupport.Inventory(directory);
        var store = new ConfigurationHostStore
        { Read = (_, _, _) => ValueTask.FromResult(ConfigurationReadResult.Failure()) };
        int constructions = 0;
        await using var app = new CISApplication(builder =>
        {
            ApplicationTestSupport.Configure(builder, inventory);
            builder.Configuration = new(ApplicationTestSupport.HostName, store) { IsHealthCritical = true };
            builder.Services.AddSingleton<IStartable>(_ =>
            { constructions++; return new ApplicationTestSupport.Participant(_ => Task.CompletedTask); });
        });
        CoreConfigurationException failure = await Assert.ThrowsExactlyAsync<CoreConfigurationException>(
            () => app.StartAsync().WaitAsync(ApplicationTestSupport.Deadline));
        Assert.IsTrue(failure.Issues.Any(issue => issue.Code == "Configuration.StoreRead"));
        Assert.AreSame(failure, app.Failure);
        Assert.AreEqual(0, constructions);
        Assert.AreEqual(0, store.Watches);
        Assert.AreEqual(0, store.Disposals);
        Assert.Throws<InvalidOperationException>(() => _ = app.Services);
    }

    [TestMethod]
    public async Task FailedBuildRetainsTheOriginalValidationOutcomeAndDisposesPreparedOwnershipWithoutTakingTheStore()
    {
        using var directory = new TemporaryDirectory();
        TypeManagerOptions inventory = ApplicationTestSupport.Inventory(directory);
        var store = new ConfigurationHostStore();
        var app = new CISApplication(builder =>
        {
            ApplicationTestSupport.Configure(builder, inventory);
            builder.Configuration = new(ApplicationTestSupport.HostName, store) { IsHealthCritical = true };
            builder.Services.AddSingleton<Unresolvable>();
        });
        Task start = app.StartAsync();
        try
        {
            Exception failure = await ApplicationTestSupport.FailureAsync(start);
            Assert.IsInstanceOfType<AggregateException>(failure);
            Assert.AreSame(failure, app.Failure);
            Assert.IsTrue(store.Reads > 0, "Configuration preparation precedes the one failing Build.");
            Assert.AreEqual(0, store.Watches);
            Assert.AreEqual(0, store.Disposals);
            Assert.Throws<InvalidOperationException>(() => _ = app.Services);
            Assert.AreSame(start, app.StartAsync());
        }
        finally { await app.DisposeAsync().AsTask().WaitAsync(ApplicationTestSupport.Deadline); }
        Assert.AreEqual(0, store.Disposals);
    }

    [TestMethod]
    public async Task AReplacedHealthInstanceOwnsBothStartupAndTheActualReadinessTransport()
    {
        using var directory = new TemporaryDirectory();
        TypeManagerOptions inventory = ApplicationTestSupport.Inventory(directory);
        await using var health = new HealthcheckService(new HealthcheckOptions());
        await using IConditionMonitor monitor = health.CreateCondition(new() { Name = "replacement", IsCritical = true, InitiallyFlagged = true });
        await using CISApplication app = CISApplication.CreateWeb([], builder =>
        {
            ApplicationTestSupport.Configure(builder, inventory);
            builder.Healthcheck = new HealthcheckOptions { MaxMonitors = 0 };
            builder.Services.Replace(ServiceDescriptor.Singleton(health));
        }, web => web.MapGet("/business", () => "business"));
        await app.StartAsync().WaitAsync(ApplicationTestSupport.Deadline);
        Assert.AreSame(health, app.Services.GetRequiredService<HealthcheckService>());
        using HttpClient client = ApplicationTestSupport.Client(app);
        await ApplicationTestSupport.StatusAsync(client, "/_health/ready", HttpStatusCode.InternalServerError);
        await ApplicationTestSupport.StatusAsync(client, "/business", HttpStatusCode.ServiceUnavailable);
        monitor.Clear();
        await ApplicationTestSupport.StatusAsync(client, "/_health/ready", HttpStatusCode.OK);
        await ApplicationTestSupport.StatusAsync(client, "/business", HttpStatusCode.OK);
        Assert.IsTrue(health.CaptureSnapshot().Ready);
    }

    [TestMethod]
    public async Task RemoveAllKeepsDefaultHealthAndHeartbeatAbsentAndNoRestSelectionCreatesNoOwner()
    {
        using var directory = new TemporaryDirectory();
        TypeManagerOptions inventory = ApplicationTestSupport.Inventory(directory);
        await using var app = new CISApplication(builder =>
        {
            ApplicationTestSupport.Configure(builder, inventory);
            builder.Services.RemoveAll<HealthcheckService>();
            builder.Services.RemoveAll<HeartbeatRuntime>();
        });
        await app.StartAsync().WaitAsync(ApplicationTestSupport.Deadline);
        Assert.IsNull(app.Services.GetService<HealthcheckService>());
        Assert.IsNull(app.Services.GetService<HeartbeatRuntime>());
        Assert.IsNull(app.Services.GetService<IRestClientRuntime>());
        ConfigurationRuntime configuration = app.Services.GetRequiredService<ConfigurationRuntime>();
        Assert.IsTrue(configuration.CaptureStatus().ActivationCompleted);
        Assert.AreEqual(ConfigurationWatchState.NotRequired, configuration.CaptureStatus().WatchState);
        CollectionAssert.AreEquivalent(new[] { typeof(ConfigurationHostSettings), typeof(ConfigurationHostDirectSettings) },
            app.Services.GetRequiredService<ITypeManager>().GetImplementations<IConfigurationObject>().ToArray());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LoggingFactoryOwnershipJoinsActualWritesBeforeRootDisposalWhileAnInstanceRegistrationRemainsBorrowed(bool borrowed)
    {
        using var directory = new TemporaryDirectory();
        TypeManagerOptions inventory = ApplicationTestSupport.Inventory(directory);
        var dependency = new ApplicationTestSupport.Dependency();
        var externalDependency = new ApplicationTestSupport.Dependency();
        ApplicationTestSupport.HeldLogWriter? writer = borrowed ? new(externalDependency) : null;
        CoreLogRuntime? runtime = borrowed ? ApplicationTestSupport.LoggingRuntime(writer!) : null;
        var app = new CISApplication(builder =>
        {
            ApplicationTestSupport.Configure(builder, inventory);
            ApplicationTestSupport.RegisterDependency(builder.Services, dependency);
            if (borrowed) { builder.Services.Replace(ServiceDescriptor.Singleton(runtime!)); }
            else
            {
                builder.Services.AddSingleton<ApplicationTestSupport.HeldLogWriter>(root => writer = new ApplicationTestSupport.HeldLogWriter(
                    root.GetRequiredService<ApplicationTestSupport.Dependency>()));
                builder.Services.Replace(ServiceDescriptor.Singleton<CoreLogRuntime>(root =>
                    runtime = ApplicationTestSupport.LoggingRuntime(root.GetRequiredService<ApplicationTestSupport.HeldLogWriter>())));
            }
        });
        Task? disposal = null;
        try
        {
            await app.StartAsync().WaitAsync(ApplicationTestSupport.Deadline);
            CoreLogRuntime selected = app.Services.GetRequiredService<CoreLogRuntime>();
            Assert.AreSame(runtime, selected);
            Assert.AreEqual(LogWriteOutcome.Accepted, selected.CreateLogger("Application.Lifetime").Write(CoreLogLevel.Information,
                new LogEventData { EventName = "held", Message = "owned operation" }).Outcome);
            await writer!.Entered.Task.WaitAsync(ApplicationTestSupport.Deadline);
            disposal = app.DisposeAsync().AsTask();
            if (borrowed)
            {
                await disposal.WaitAsync(ApplicationTestSupport.Deadline);
                Assert.AreEqual(1, dependency.Disposals);
                Assert.IsFalse(selected.Completion.IsCompleted);
                Assert.IsNull(selected.GetDiagnostics().LastStopResult, "The facade must not stop an instance-owned logging runtime.");
                Assert.AreEqual(LogWriteOutcome.Accepted, selected.CreateLogger("Application.Lifetime").Write(CoreLogLevel.Information,
                    new LogEventData { EventName = "still-open", Message = "external owner" }).Outcome);
                Assert.AreEqual(0, writer.Disposals);
                Assert.AreEqual(0, externalDependency.Disposals);
                writer.Release.TrySetResult();
                await selected.StopAsync(CancellationToken.None).WaitAsync(ApplicationTestSupport.Deadline);
                await selected.Completion.WaitAsync(ApplicationTestSupport.Deadline);
                Assert.IsTrue(writer.UsedDependency);
            }
            else
            {
                await ConfigurationHostTestSupport.ObserveAsync(
                    () => selected.GetDiagnostics().LastStopResult?.Completion == LogCompletion.TimedOut,
                    "The facade must reach the real runtime's bounded stop observation.");
                Assert.IsFalse(selected.Completion.IsCompleted);
                Assert.IsFalse(disposal.IsCompleted, "A bounded stop result cannot release the DI-owned writer/dependency.");
                Assert.AreEqual(0, writer.Disposals);
                Assert.AreEqual(0, dependency.Disposals);
                writer.Release.TrySetResult();
                await disposal.WaitAsync(ApplicationTestSupport.Deadline);
                Assert.IsTrue(selected.Completion.IsCompletedSuccessfully);
                Assert.IsTrue(writer.UsedDependency);
                Assert.AreEqual(1, writer.Disposals);
                Assert.AreEqual(1, dependency.Disposals);
            }
        }
        finally
        {
            writer?.Release.TrySetResult();
            if (runtime is not null)
            {
                await runtime.StopAsync(CancellationToken.None).WaitAsync(ApplicationTestSupport.Deadline);
                await runtime.Completion.WaitAsync(ApplicationTestSupport.Deadline);
            }
            await (disposal ?? app.DisposeAsync().AsTask()).WaitAsync(ApplicationTestSupport.Deadline);
            if (borrowed && writer is not null) { await writer.DisposeAsync(); }
            await externalDependency.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task RouteMappingFailureBeforeHostedStartStillCutsOffRestAndJoinsItsActualCleanupBeforeRootDisposal()
    {
        await using var server = await RestHttpLocalServer.StartAsync(context => context.Response.WriteAsync("done", context.RequestAborted));
        using var directory = new TemporaryDirectory();
        TypeManagerOptions inventory = ApplicationTestSupport.Inventory(directory);
        var dependency = new ApplicationTestSupport.Dependency();
        var cleanup = new ApplicationTestSupport.HeldResponseCleanup(dependency);
        var options = new RestClientOptions("route") { ResponseFormat = RestResponseFormat.Text };
        options.Handlers.Add(cleanup);
        var original = new InvalidOperationException("route composition failed");
        Task<RestResult<string>>? call = null;
        IRestClient? client = null;
        CISApplication app = CISApplication.CreateWeb([], builder =>
        {
            ApplicationTestSupport.Configure(builder, inventory);
            ApplicationTestSupport.RegisterDependency(builder.Services, dependency);
            builder.RestClients.Add(options);
        }, web =>
        {
            Assert.AreSame(dependency, web.Services.GetRequiredService<ApplicationTestSupport.Dependency>());
            client = web.Services.GetRequiredKeyedService<IRestClient>("route");
            call = client.Get<string>(server.Address);
            throw original;
        });
        Task start = app.StartAsync();
        Task? stop = null, disposal = null;
        try
        {
            Assert.AreSame(original, await ApplicationTestSupport.FailureAsync(start));
            await cleanup.Entered.Task.WaitAsync(ApplicationTestSupport.Deadline);
            stop = app.StopAsync();
            await cleanup.CancellationObserved.Task.WaitAsync(ApplicationTestSupport.Deadline);
            disposal = app.DisposeAsync().AsTask();
            Assert.IsFalse(stop.IsCompleted);
            Assert.IsFalse(disposal.IsCompleted);
            Assert.IsFalse(call!.IsCompleted);
            Assert.AreEqual(0, dependency.Disposals);
            RestResult<string> rejected = await client!.Get<string>(server.Address).WaitAsync(ApplicationTestSupport.Deadline);
            Assert.AreEqual(RestFailureKind.Stopping, rejected.Context.FailureKind);
            Assert.AreEqual(0, rejected.Context.AttemptCount);
            cleanup.Release.TrySetResult();
            RestResult<string> settled = await call.WaitAsync(ApplicationTestSupport.Deadline);
            Assert.AreEqual(RestFailureKind.Stopping, settled.Context.FailureKind);
            await stop.WaitAsync(ApplicationTestSupport.Deadline);
            await disposal.WaitAsync(ApplicationTestSupport.Deadline);
            Assert.AreEqual(1, cleanup.Disposals);
            Assert.AreEqual(1, dependency.Disposals);
        }
        finally
        {
            cleanup.Release.TrySetResult();
            await ApplicationTestSupport.SettleAsync(call);
            await ApplicationTestSupport.SettleAsync(start);
            await (stop ?? app.StopAsync()).WaitAsync(ApplicationTestSupport.Deadline);
            await (disposal ?? app.DisposeAsync().AsTask()).WaitAsync(ApplicationTestSupport.Deadline);
        }
    }

    [TestMethod]
    public async Task FacadeStopClosesRestBeforeJoiningAHeartbeatCallbackAndRetainsDependenciesThroughActualCleanup()
    {
        await using var server = await RestHttpLocalServer.StartAsync(context => context.Response.WriteAsync("done", context.RequestAborted));
        using var directory = new TemporaryDirectory();
        TypeManagerOptions inventory = ApplicationTestSupport.Inventory(directory);
        var dependency = new ApplicationTestSupport.Dependency();
        var cleanup = new ApplicationTestSupport.HeldResponseCleanup(dependency);
        var options = new RestClientOptions("poll") { ResponseFormat = RestResponseFormat.Text };
        options.Handlers.Add(cleanup);
        HeartbeatOwner? owner = null;
        StartupSubscriber? subscriber = null;
        var result = new TaskCompletionSource<RestResult<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (CISApplication app = CISApplication.CreateWeb([], builder =>
        {
            ApplicationTestSupport.Configure(builder, inventory);
            ApplicationTestSupport.RegisterDependency(builder.Services, dependency);
            builder.RestClients.Add(options);
        }, web =>
        {
            IRestClient rest = web.Services.GetRequiredKeyedService<IRestClient>("poll");
            owner = web.Services.GetRequiredService<HeartbeatRuntime>().CreateOwner();
            subscriber = new StartupSubscriber(async token =>
            {
                RestResult<string> value = await rest.Get<string>(server.Address, cancellationToken: token);
                result.TrySetResult(value);
                return value.Success;
            });
            owner.Service.Register(subscriber, new() { Interval = TimeSpan.FromDays(1) });
            web.MapGet("/business", () => "business");
        }))
        {
            Task? stop = null;
            try
            {
                await app.StartAsync().WaitAsync(ApplicationTestSupport.Deadline);
                await cleanup.Entered.Task.WaitAsync(ApplicationTestSupport.Deadline);
                Task canceledWait = app.StopAsync(new CancellationToken(canceled: true));
                stop = app.StopAsync();
                await Assert.ThrowsAsync<OperationCanceledException>(() => canceledWait);
                await cleanup.CancellationObserved.Task.WaitAsync(ApplicationTestSupport.Deadline);
                Assert.IsFalse(stop.IsCompleted);
                Assert.IsFalse(dependency.Stopping.Task.IsCompleted);
                Assert.AreEqual(0, dependency.Disposals);
                cleanup.Release.TrySetResult();
                await stop.WaitAsync(ApplicationTestSupport.Deadline);
                RestResult<string> settled = await result.Task.WaitAsync(ApplicationTestSupport.Deadline);
                Assert.AreEqual(RestFailureKind.Stopping, settled.Context.FailureKind);
                Assert.AreEqual(1, subscriber!.Calls);
                Assert.AreEqual(0, subscriber.Errors);
                Assert.AreEqual(1, cleanup.Disposals);
                Assert.IsTrue(dependency.Stopping.Task.IsCompleted);
                Assert.AreEqual(0, dependency.Disposals);
            }
            finally
            {
                cleanup.Release.TrySetResult();
                await (stop ?? app.StopAsync()).WaitAsync(ApplicationTestSupport.Deadline);
                if (owner is not null) { await owner.DisposeAsync().AsTask().WaitAsync(ApplicationTestSupport.Deadline); }
                GC.KeepAlive(subscriber);
            }
        }
        Assert.AreEqual(1, dependency.Disposals);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RunReturnsZeroForOrdinaryHostShutdownAndRecognizedRequestedCancellation(bool cancel)
    {
        using var directory = new TemporaryDirectory();
        TypeManagerOptions inventory = ApplicationTestSupport.Inventory(directory);
        using var cancellation = new CancellationTokenSource();
        await using var app = new CISApplication(builder => ApplicationTestSupport.Configure(builder, inventory));
        Task<int> run = app.RunAsync(cancellation.Token);
        try
        {
            await app.StartAsync().WaitAsync(ApplicationTestSupport.Deadline);
            Assert.IsFalse(run.IsCompleted);
            if (cancel) { cancellation.Cancel(); }
            else { app.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication(); }
            Assert.AreEqual(0, await run.WaitAsync(ApplicationTestSupport.Deadline));
            Assert.IsNull(app.Failure);
            Assert.AreSame(app.StopAsync(), app.StopAsync());
        }
        finally
        {
            await app.StopAsync().WaitAsync(ApplicationTestSupport.Deadline);
            await ApplicationTestSupport.SettleAsync(run);
        }
    }

    private sealed class CancelAfterStartupFailure(CancellationTokenSource cancellation) : ILoggerProvider
    {
        private int canceled;
        internal TaskCompletionSource Classified { get; } = ApplicationTestSupport.Signal();
        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, this);
        public void Dispose() { }
        private sealed class Logger(string category, CancelAfterStartupFailure owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Error;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (category != typeof(StartupCoordinator).FullName || logLevel != LogLevel.Error
                    || state is not IEnumerable<KeyValuePair<string, object?>> fields
                    || !fields.Any(pair => pair.Key == "Outcome" && Equals(pair.Value, "Failed"))
                    || Interlocked.Exchange(ref owner.canceled, 1) != 0) { return; }
                owner.Cancel();
            }
        }
        private void Cancel()
        {
            cancellation.Cancel();
            Classified.TrySetResult();
        }
    }

    private sealed class FirstParticipant(Func<CancellationToken, Task> start) : IStartable
    {
        internal int Calls { get; private set; }
        public Task StartAsync(CancellationToken cancellationToken)
        { Calls++; return start(cancellationToken); }
    }

    private sealed class SecondParticipant(Func<CancellationToken, Task> start) : IStartable
    {
        internal int Calls { get; private set; }
        public Task StartAsync(CancellationToken cancellationToken)
        { Calls++; return start(cancellationToken); }
    }

    private interface IUnavailable { }
    private sealed class Unresolvable(IUnavailable missing)
    {
        internal IUnavailable Missing { get; } = missing;
    }

    private sealed class RootReference
    {
        public RootReference() { }
    }
}

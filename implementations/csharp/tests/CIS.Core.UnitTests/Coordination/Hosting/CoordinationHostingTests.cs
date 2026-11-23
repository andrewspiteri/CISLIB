using System.Collections.Concurrent;
using CIS.CommonTesting;
using CIS.Configuration;
using CIS.Coordination;
using CIS.Coordination.Hosting;
using CIS.Healthcheck;
using CIS.Heartbeat.Hosting;
using CIS.Jobs;
using CIS.Jobs.Hosting;
using CIS.Startup;
using CIS.Startup.Hosting;
using CIS.Tests.UnitTests.Jobs;
using CIS.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Coordination.Hosting;

[TestClass]
public sealed class CoordinationHostingTests
{
    [TestMethod]
    public async Task NativeHostStartupIsSequentialAndRegistrationDoesNotValidateOrConstructHandlers()
    {
        await using var fixture = new HostedFixture();
        fixture.Core.EnqueueGrant();
        fixture.Core.Control.Callback = (_, _) =>
        {
            fixture.Order.Enqueue("handler");
            return Task.FromResult(new ContinuousIterationResult(ContinuousIterationOutcome.Idle));
        };
        Assert.AreEqual(0, fixture.Provider.Validations);
        Assert.AreEqual(0, fixture.Core.Control.CreatedScopes);
        await fixture.Host.StartAsync();
        await JobRuntimeTestSupport.UntilAsync(() => fixture.Core.Control.Invocations.Count == 1);
        CollectionAssert.AreEqual(new[] { "business", "authority", "configuration", "handler" }, fixture.Order.ToArray());
        Assert.AreEqual(1, fixture.Provider.Validations);
    }

    [TestMethod]
    public async Task RawApplicationStoppingClosesReadinessButKeepsHealthAdmissionUsableUntilTheJobActuallyDrains()
    {
        await using var fixture = new HostedFixture();
        fixture.Core.EnqueueGrant();
        bool createdDuringDrain = false;
        fixture.Core.Control.Callback = async (_, _) =>
        {
            await fixture.Core.Control.WorkRelease.Task;
            await using IConditionMonitor condition = fixture.Health.CreateCondition(new()
            {
                Name = "Final guarded work",
                IsCritical = false,
                InitiallyFlagged = false
            });
            createdDuringDrain = true;
            return new(ContinuousIterationOutcome.Idle);
        };
        await fixture.Host.StartAsync();
        await fixture.Core.Control.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await JobRuntimeTestSupport.UntilAsync(() => fixture.Health.CaptureSnapshot().Ready);
        fixture.Host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
        Assert.IsFalse(fixture.Health.CaptureSnapshot().Ready);
        Task stop = fixture.Host.StopAsync();
        Assert.IsFalse(stop.IsCompleted);
        Assert.AreEqual(1, fixture.Core.Runtime.CaptureStatus().ActiveContinuousInvocations);
        fixture.Core.Control.WorkRelease.TrySetResult();
        await stop.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsTrue(createdDuringDrain);
        Assert.IsFalse(fixture.Health.CaptureSnapshot().Ready);
        Assert.Throws<InvalidOperationException>(() => fixture.Health.CreateCondition(new() { IsCritical = false, InitiallyFlagged = false }));
    }

    [TestMethod]
    public async Task LostUncooperativeWorkKeepsTheNativeHostStopAndScopeBarrierPending()
    {
        await using var fixture = new HostedFixture();
        ManualCoordinationLease lease = fixture.Core.EnqueueGrant();
        fixture.Core.Control.Callback = async (_, _) =>
        {
            await fixture.Core.Control.WorkRelease.Task;
            return new(ContinuousIterationOutcome.Idle);
        };
        await fixture.Host.StartAsync();
        await fixture.Core.Control.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task stop = fixture.Host.StopAsync();
        await lease.LoseAsync(CoordinationLossReason.ShutdownDeadlineExceeded);
        Assert.IsFalse(stop.IsCompleted);
        Assert.AreEqual(1, fixture.Core.Runtime.CaptureStatus().ActiveContinuousInvocations);
        Assert.AreEqual(1, fixture.Core.Runtime.CaptureStatus().DutyReservations);
        Assert.AreEqual(2, fixture.Core.Control.DisposedScopes);
        fixture.Core.Control.WorkRelease.TrySetResult();
        await stop.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(fixture.Core.Control.CreatedScopes, fixture.Core.Control.DisposedScopes);
    }

    [TestMethod]
    public async Task ACompletedStartupCallerTokenDoesNotBecomeTheDutyLifetimeAndCapacityPulsesDoNotReplayFaults()
    {
        await using var fixture = new HostedFixture();
        fixture.Core.EnqueueGrant();
        fixture.Core.Control.Callback = (_, _) => Task.FromResult(new ContinuousIterationResult(ContinuousIterationOutcome.Failed));
        using var startup = new CancellationTokenSource();
        await fixture.Host.StartAsync(startup.Token);
        await JobRuntimeTestSupport.UntilAsync(() => fixture.Core.Runtime.CaptureStatus().DutyReservations == 0);
        startup.Cancel();
        Assert.IsFalse(fixture.Core.Runtime.CaptureStatus().Stopping);
        Assert.IsTrue(fixture.Core.Runtime.TryApplyConfiguration(fixture.Core.Configuration(1)));
        JobHostingSupervisor supervisor = fixture.Host.Services.GetRequiredService<JobHostingSupervisor>();
        await JobRuntimeTestSupport.UntilAsync(() => supervisor.ReconciledRevision >= 1);
        Assert.AreEqual(1, fixture.Core.Coordination.Calls.Count);
        Assert.AreEqual(1, fixture.Core.Control.Invocations.Count);
    }

    [TestMethod]
    public async Task ExplicitDisableEnableTransitionWaitsForTheOldKeyAndScopeBeforeReactivation()
    {
        await using var fixture = new HostedFixture();
        ManualCoordinationLease first = fixture.Core.EnqueueGrant();
        fixture.Core.Control.HoldCleanup = true;
        fixture.Core.Control.Callback = async (_, _) =>
        {
            await fixture.Core.Control.WorkRelease.Task;
            return new(ContinuousIterationOutcome.Idle);
        };
        await fixture.Host.StartAsync();
        await fixture.Core.Control.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsTrue(fixture.Core.Runtime.TryApplyConfiguration(fixture.Core.Configuration(1, enabled: false)));
        ManualCoordinationLease second = fixture.Core.EnqueueGrant();
        Assert.IsTrue(fixture.Core.Runtime.TryApplyConfiguration(fixture.Core.Configuration(2)));
        JobHostingSupervisor supervisor = fixture.Host.Services.GetRequiredService<JobHostingSupervisor>();
        await JobRuntimeTestSupport.UntilAsync(() => supervisor.ReconciledRevision >= 2);
        fixture.Core.Control.WorkRelease.TrySetResult();
        await fixture.Core.Control.ScopeCleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(1, fixture.Core.Coordination.Calls.Count);
        Assert.AreEqual(1, fixture.Core.Runtime.CaptureStatus().ActiveContinuousInvocations);
        fixture.Core.Control.ScopeCleanupRelease.TrySetResult();
        await JobRuntimeTestSupport.UntilAsync(() => fixture.Core.Control.Invocations.Count == 2);
        Assert.AreEqual(CoordinationLeaseState.Released, first.State);
        Assert.AreEqual(CoordinationLeaseState.Owned, second.State);
    }

    [TestMethod]
    [DataRow("Host")]
    [DataRow("Category")]
    [DataRow("Job")]
    public async Task CoalescedDisableEnableTransitionsRestartExactlyOnceAfterActualSettlement(string disabledGate)
    {
        await using var core = new JobRuntimeTestSupport(jobs: ["wallet.compaction", "wallet.other"]);
        var secondTargetEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondTargetRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var otherWorkRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int blockedScopes = 0;
        int walletCallbacks = 0;
        core.Control.HoldCleanup = true;
        core.Control.BeforeScope = async (request, _) =>
        {
            if (request.JobId == "wallet.other" && Interlocked.CompareExchange(ref blockedScopes, 1, 0) == 0)
            {
                secondTargetEntered.TrySetResult();
                // Deliberately hold physical scope creation even if an intermediate configuration requests drain.
                await secondTargetRelease.Task;
            }
        };
        core.Control.Callback = async (context, _) =>
        {
            if (context.Definition.JobId == "wallet.other")
            {
                await otherWorkRelease.Task;
                return new(ContinuousIterationOutcome.Idle);
            }

            if (Interlocked.Increment(ref walletCallbacks) == 1)
            {
                await core.Control.WorkRelease.Task;
                return new(ContinuousIterationOutcome.Idle);
            }

            return new(ContinuousIterationOutcome.Failed);
        };
        ManualCoordinationLease first = core.EnqueueGrant();
        core.EnqueueGrant("wallet.other");
        ManualCoordinationLease replacement = core.EnqueueGrant();
        await core.Runtime.StartAsync();
        await using var supervisor = new JobHostingSupervisor(core.Runtime, new()
        {
            ContinuousTargets =
            [
                new("wallet-service", "wallet.compaction", "partition", JobExecutionMode.Continuous),
                new("wallet-service", "wallet.other", "partition", JobExecutionMode.Continuous)
            ]
        });

        try
        {
            Task startup = supervisor.StartAsync(CancellationToken.None);
            await secondTargetEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(1, Volatile.Read(ref walletCallbacks));
            Assert.IsTrue(core.Runtime.TryApplyConfiguration(Settings(1, enabled: false)));
            Assert.IsTrue(core.Runtime.TryApplyConfiguration(Settings(2, enabled: true)));
            Assert.AreEqual(-1L, supervisor.ReconciledRevision,
                "Both accepted transitions must precede the next reconciliation of the first target.");

            secondTargetRelease.TrySetResult();
            await startup.WaitAsync(TimeSpan.FromSeconds(10));
            await JobRuntimeTestSupport.UntilAsync(() => supervisor.ReconciledRevision >= 2);
            core.Control.WorkRelease.TrySetResult();
            await core.Control.ScopeCleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(1, core.Coordination.Calls.Count(call => call.DutyId == "wallet.compaction"),
                "A replacement must wait for the old callback and its actual scope cleanup.");

            core.Control.ScopeCleanupRelease.TrySetResult();
            await JobRuntimeTestSupport.UntilAsync(() => replacement.State == CoordinationLeaseState.Released);
            Assert.AreEqual(CoordinationLeaseState.Released, first.State);
            Assert.AreEqual(2, Volatile.Read(ref walletCallbacks));
            Assert.AreEqual(2, core.Coordination.Calls.Count(call => call.DutyId == "wallet.compaction"));

            Assert.IsTrue(core.Runtime.TryApplyConfiguration(Settings(long.MaxValue, enabled: true)));
            await JobRuntimeTestSupport.UntilAsync(() => supervisor.ReconciledRevision == long.MaxValue);
            Assert.AreEqual(2, Volatile.Read(ref walletCallbacks),
                "A later settings revision and capacity notifications must not replay the replacement's failure.");
            Assert.AreEqual(2, core.Coordination.Calls.Count(call => call.DutyId == "wallet.compaction"));
        }
        finally
        {
            secondTargetRelease.TrySetResult();
            core.Control.WorkRelease.TrySetResult();
            core.Control.ScopeCleanupRelease.TrySetResult();
            otherWorkRelease.TrySetResult();
        }

        JobConfigurationSnapshot Settings(long revision, bool enabled)
        {
            return disabledGate switch
            {
                "Host" => core.Configuration(revision, enabled: enabled),
                "Category" => core.Configuration(revision, categories: [new("wallet-service", "maintenance", enabled: enabled)]),
                _ => core.Configuration(revision, settings: id => new("wallet-service", id,
                    new(JobExecutionMode.Continuous), enabled: id != "wallet.compaction" || enabled))
            };
        }
    }

    [TestMethod]
    public async Task AStaleReconciliationCannotActivateANewEpochAndReplayItsSynchronouslyFailedCallback()
    {
        await using var core = new JobRuntimeTestSupport(jobs: ["wallet.compaction", "wallet.other"]);
        var firstScopeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstScopeRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int blockedScopes = 0;
        int otherCallbacks = 0;
        core.Control.BeforeScope = async (request, _) =>
        {
            if (request.JobId == "wallet.compaction" && Interlocked.CompareExchange(ref blockedScopes, 1, 0) == 0)
            {
                firstScopeEntered.TrySetResult();
                await firstScopeRelease.Task;
            }
        };
        core.Control.Callback = async (context, _) =>
        {
            if (context.Definition.JobId == "wallet.other")
            {
                Interlocked.Increment(ref otherCallbacks);
                return new(ContinuousIterationOutcome.Failed);
            }

            await core.Control.WorkRelease.Task;
            return new(ContinuousIterationOutcome.Idle);
        };
        core.EnqueueGrant();
        core.EnqueueGrant("wallet.other");
        await using ManualCoordinationLease unexpectedReplay = core.EnqueueGrant("wallet.other");
        await core.Runtime.StartAsync();
        await using var supervisor = new JobHostingSupervisor(core.Runtime, new()
        {
            ContinuousTargets =
            [
                new("wallet-service", "wallet.compaction", "partition", JobExecutionMode.Continuous),
                new("wallet-service", "wallet.other", "partition", JobExecutionMode.Continuous)
            ]
        });

        try
        {
            Task startup = supervisor.StartAsync(CancellationToken.None);
            await firstScopeEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsTrue(core.Runtime.TryApplyConfiguration(Settings(1, otherEnabled: false)));
            Assert.IsTrue(core.Runtime.TryApplyConfiguration(Settings(2, otherEnabled: true)));
            Assert.AreEqual(1, core.Runtime.CaptureStatus().DutyReservations,
                "The second target's configuration changes precede its first admission.");
            firstScopeRelease.TrySetResult();
            await startup.WaitAsync(TimeSpan.FromSeconds(10));
            await JobRuntimeTestSupport.UntilAsync(() => supervisor.ReconciledRevision >= 2
                && core.Runtime.CaptureStatus().DutyReservations == 1 && Volatile.Read(ref otherCallbacks) >= 1);
            Assert.IsTrue(core.Runtime.TryApplyConfiguration(Settings(3, otherEnabled: true)));
            await JobRuntimeTestSupport.UntilAsync(() => supervisor.ReconciledRevision >= 3);
            Assert.AreEqual(1, Volatile.Read(ref otherCallbacks));
            Assert.AreEqual(1, core.Coordination.Calls.Count(call => call.DutyId == "wallet.other"));
            Assert.AreEqual(1, core.Coordination.PendingScripts,
                "The failed activation consumes its exact admitted epoch, even if it finishes before Start returns.");
        }
        finally
        {
            firstScopeRelease.TrySetResult();
            core.Control.WorkRelease.TrySetResult();
        }

        JobConfigurationSnapshot Settings(long revision, bool otherEnabled)
            => core.Configuration(revision, settings: id => new("wallet-service", id,
                new(JobExecutionMode.Continuous), enabled: id != "wallet.other" || otherEnabled));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task FailedActivationScopeCleanupIsReportedAndDoesNotReplayWithinItsEpoch(bool initial)
    {
        await using var core = new JobRuntimeTestSupport();
        core.Control.ThrowStartCleanup = true;
        if (!initial)
        {
            Assert.IsTrue(core.Runtime.TryApplyConfiguration(core.Configuration(1, enabled: false)));
        }

        await core.Runtime.StartAsync();
        await using var supervisor = new JobHostingSupervisor(core.Runtime, new()
        {
            ContinuousTargets = [new("wallet-service", "wallet.compaction", "partition", JobExecutionMode.Continuous)]
        });
        if (initial)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => supervisor.StartAsync(CancellationToken.None));
        }
        else
        {
            await supervisor.StartAsync(CancellationToken.None);
            Assert.IsTrue(core.Runtime.TryApplyConfiguration(core.Configuration(2)));
            await JobRuntimeTestSupport.UntilAsync(() => supervisor.ReconciledRevision >= 2);
            Assert.IsTrue(core.Runtime.TryApplyConfiguration(core.Configuration(3)));
            await JobRuntimeTestSupport.UntilAsync(() => supervisor.ReconciledRevision >= 3);
        }

        Assert.AreEqual("jobs.host_activation_failed", supervisor.LastFailure);
        Assert.AreEqual(1, core.Control.CreatedScopes);
        Assert.AreEqual(1, core.Control.DisposedScopes);
        Assert.AreEqual(0, core.Coordination.Calls.Count);
        Assert.AreEqual(0, core.Control.Invocations.Count);
        Assert.IsTrue(core.Runtime.CaptureHealth().Unhealthy);
    }

    [TestMethod]
    public async Task RequiredAuthorityFailurePreventsJobActivationAndStillDrainsTheFailedNativeHost()
    {
        await using var fixture = new HostedFixture();
        fixture.Provider.ValidationFailure = new InvalidOperationException("Controlled unavailable authority.");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Host.StartAsync());
        Assert.AreEqual(0, fixture.Core.Control.Invocations.Count);
        await fixture.Host.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsFalse(fixture.Health.CaptureSnapshot().Ready);
    }

    [TestMethod]
    public async Task FactoryCreationIsJoinedAndDeferredCallbackFailureDoesNotTurnThePhysicalBarrierIntoACycle()
    {
        var lifetime = new CoordinationHostLifetime();
        using IDisposable constructing = lifetime.EnterBinding();
        var provider = new ValidationOnlyProvider(new());
        await using var runtime = new CoordinationRuntime(provider, new());
        Task stop = lifetime.StopAsync();
        Assert.IsFalse(lifetime.WaitForDependenciesAsync().IsCompleted);
        lifetime.Bind(runtime);
        Assert.AreEqual(CoordinationRuntimeState.Stopping, runtime.GetSnapshot().State);
        using CancellationTokenRegistration callback = lifetime.DependencyStopping.Register(() =>
        {
            Assert.IsTrue(lifetime.IsDrained);
            lifetime.WaitForDependenciesAsync().GetAwaiter().GetResult();
            throw new InvalidOperationException("Controlled deferred-cutoff fault.");
        });
        constructing.Dispose();
        Exception? observed = await CoordinationHostLifetime.CaptureStopAsync(lifetime);
        Assert.IsNotNull(observed);
        Assert.IsTrue(stop.IsCompleted);
        await lifetime.WaitForDependenciesAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.ThrowsExactly<InvalidOperationException>(() => lifetime.EnterBinding());
        try
        {
            await lifetime.DisposeAsync();
        }
        catch (Exception) { /* The original Stop failure is retained and asserted above. */ }
    }

    [TestMethod]
    public void AbsentCoordinationUsesTheOriginalRawTokenAndAnUnexpectedTailIsRejected()
    {
        using ServiceProvider absent = new ServiceCollection().BuildServiceProvider();
        using var raw = new CancellationTokenSource();
        Assert.AreEqual(raw.Token, CoordinationHostLifetime.DependencyToken(absent, raw.Token));

        var services = new ServiceCollection();
        services.AddCisCoordination();
        services.AddSingleton<IHostedService, UnexpectedHostedService>();
        Assert.IsFalse(((CoordinationHostingOrder)services.Single(item => item.ServiceType == typeof(CoordinationHostingOrder)).ImplementationInstance!).IsValid());
    }

    private sealed class HostedFixture : IAsyncDisposable
    {
        internal readonly JobRuntimeTestSupport Core = new();
        internal readonly ConcurrentQueue<string> Order = new();
        internal readonly HealthcheckService Health = new(new());
        internal readonly ValidationOnlyProvider Provider;
        internal readonly IHost Host;

        internal HostedFixture()
        {
            Provider = new(Order);
            Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
                .UseDefaultServiceProvider(options => { options.ValidateOnBuild = true; options.ValidateScopes = true; })
                .ConfigureLogging(logging => logging.ClearProviders())
                .ConfigureServices(services =>
                {
                    services.AddSingleton(Health);
                    services.AddSingleton<IHealthMonitorFactory>(Health);
                    services.AddSingleton<TimeProvider>(Core.Clock);
                    services.AddSingleton<IStartable>(new StartupProbe(Order));
                    services.AddSingleton<ICoordinationProvider>(Provider);
                    services.AddSingleton<ICoordinationService>(Core.Coordination);
                    services.AddSingleton(Core.Catalog);
                    services.AddSingleton<IJobCatalog>(Core.Catalog);
                    services.AddSingleton(Core.Runtime);
                    services.AddSingleton<IConfigurationReader>(new PureReader());
                    services.AddSingleton(new HostSettings());
                    services.AddCisCoordination();
                    services.AddCisJobs(Core.Configuration(0), new JobHostingOptions
                    {
                        ContinuousTargets = [new("wallet-service", "wallet.compaction", "partition", JobExecutionMode.Continuous)]
                    });
                    services.AddCisJobConfiguration<HostSettings>((settings, revision) =>
                    {
                        Order.Enqueue("configuration");
                        return Core.Configuration(revision, enabled: settings.Enabled);
                    });
                    services.AddCisHeartbeat();
                    services.AddCisStartup();
                }).Build();
        }

        public async ValueTask DisposeAsync()
        {
            Core.Control.WorkRelease.TrySetResult();
            Core.Control.ScopeCleanupRelease.TrySetResult();
            try
            {
                await Host.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                if (Host is IAsyncDisposable owned)
                {
                    await owned.DisposeAsync();
                }
                else
                {
                    Host.Dispose();
                }
                await Core.DisposeAsync();
                await Health.DisposeAsync();
            }
        }
    }

    private sealed class StartupProbe(ConcurrentQueue<string> order) : IStartable
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            order.Enqueue("business");
            return Task.CompletedTask;
        }
    }

    private sealed class HostSettings : ConfigurationObject
    {
        public bool Enabled { get; set; } = true;
    }

    private sealed class PureReader : IConfigurationReader
    {
        public TResult Read<TConfiguration, TResult>(TConfiguration configuration, Func<TConfiguration, TResult> reader)
            where TConfiguration : class, IConfigurationObject => reader(configuration);
    }

    private sealed class ValidationOnlyProvider(ConcurrentQueue<string> order) : ICoordinationProvider, ICoordinationGuard
    {
        internal int Validations;
        internal Exception? ValidationFailure;
        public Guid AuthorityId { get; } = Guid.NewGuid();
        public Task ValidateAsync(CancellationToken cancellationToken = default)
        {
            order.Enqueue("authority");
            Interlocked.Increment(ref Validations);
            return ValidationFailure is null ? Task.CompletedTask : Task.FromException(ValidationFailure);
        }
        public Task<CoordinationAcquireResult> AcquireAsync(CoordinationAcquireRequest request, CancellationToken cancellationToken = default) => throw new AssertFailedException("Jobs use the explicit scripted consumer port in this host test.");
        public Task<CoordinationMutationResult> RenewAsync(CoordinationGrant grant, TimeSpan leaseDuration, CancellationToken cancellationToken = default) => throw new AssertFailedException();
        public Task<CoordinationMutationResult> ReleaseAsync(CoordinationGrant grant, CancellationToken cancellationToken = default) => throw new AssertFailedException();
        public Task<CoordinationObservation> ResolveAsync(CoordinationAcquisition acquisition, CancellationToken cancellationToken = default) => throw new AssertFailedException();
        public Task<CoordinationGuardResult> ValidateAsync(ILocalTransactionContext context, CoordinationGrant grant, CancellationToken cancellationToken = default) => throw new AssertFailedException("This is lifecycle proof, not SQL guard proof.");
    }

    private sealed class UnexpectedHostedService : IHostedService
    {
        public UnexpectedHostedService()
        {
        }
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

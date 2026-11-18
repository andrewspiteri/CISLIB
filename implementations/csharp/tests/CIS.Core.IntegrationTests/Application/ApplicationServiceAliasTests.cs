using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.Heartbeat.Hosting;
using CIS.Hosting;
using CIS.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Application;

[TestClass]
[DoNotParallelize]
public sealed class ApplicationServiceAliasTests
{
    [TestMethod]
    public async Task DefaultAliasesShareTheActualSingletonsAndOwnedCleanupIsIdempotent()
    {
        using var directory = new TemporaryDirectory();
        var inventory = ApplicationTestSupport.Inventory(directory);
        await using var app = new CISApplication(builder => ApplicationTestSupport.Configure(builder, inventory));
        try
        {
            await app.StartAsync().WaitAsync(ApplicationTestSupport.Deadline);
            HealthcheckService health = app.Services.GetRequiredService<HealthcheckService>();
            HeartbeatRuntime heartbeat = app.Services.GetRequiredService<HeartbeatRuntime>();
            Assert.AreSame(health, app.Services.GetRequiredService<IHealthMonitorFactory>());
            Assert.AreSame(health, app.Services.GetRequiredService<IHealthSnapshotSource>());
            Assert.AreSame(heartbeat, app.Services.GetRequiredService<IHeartbeatOwnerFactory>());
            await using IConditionMonitor monitor = health.CreateCondition(new()
            {
                Name = "Owned alias lifetime",
                IsCritical = false,
                InitiallyFlagged = false
            });

            await app.StopAsync().WaitAsync(ApplicationTestSupport.Deadline);
            await app.DisposeAsync();
            await app.DisposeAsync();
            await monitor.WaitForDisposalAsync().WaitAsync(ApplicationTestSupport.Deadline);
            Assert.ThrowsExactly<ObjectDisposedException>(monitor.Clear);
            Assert.ThrowsExactly<ObjectDisposedException>(() => heartbeat.CreateOwner());
        }
        finally
        {
            await app.StopAsync().WaitAsync(ApplicationTestSupport.Deadline);
        }
    }

    [TestMethod]
    public async Task FinalBorrowedConcreteInstancesHaveBorrowedDefaultAliasesToo()
    {
        using var directory = new TemporaryDirectory();
        var inventory = ApplicationTestSupport.Inventory(directory);
        await using var health = new HealthcheckService(new HealthcheckOptions());
        await using var heartbeat = new HeartbeatRuntime(new HeartbeatOptions { MaxConcurrent = 1 });
        await using IConditionMonitor monitor = health.CreateCondition(new()
        {
            IsCritical = false,
            InitiallyFlagged = false
        });
        await using var app = new CISApplication(builder =>
        {
            ApplicationTestSupport.Configure(builder, inventory);
            builder.Healthcheck = new HealthcheckOptions { MaxMonitors = 0 };
            builder.Services.Replace(ServiceDescriptor.Singleton(health));
            builder.Services.Replace(ServiceDescriptor.Singleton(heartbeat));
        });
        try
        {
            await app.StartAsync().WaitAsync(ApplicationTestSupport.Deadline);
            Assert.AreSame(health, app.Services.GetRequiredService<IHealthMonitorFactory>());
            Assert.AreSame(health, app.Services.GetRequiredService<IHealthSnapshotSource>());
            Assert.AreSame(heartbeat, app.Services.GetRequiredService<IHeartbeatOwnerFactory>());
            await app.DisposeAsync();

            // Hosting stops the selected modules. DI must not acquire disposal
            // ownership of an external instance through an automatic factory alias.
            monitor.Clear();
            Assert.AreEqual(1, health.CaptureSnapshot().MonitorCounts.Active);
            Assert.ThrowsExactly<InvalidOperationException>(() => heartbeat.CreateOwner());
        }
        finally
        {
            await app.StopAsync().WaitAsync(ApplicationTestSupport.Deadline);
        }
    }

    [TestMethod]
    public async Task RemovingOnlyBusinessAliasesDoesNotRestoreThemDuringLateHostingSetup()
    {
        using var directory = new TemporaryDirectory();
        var inventory = ApplicationTestSupport.Inventory(directory);
        await using var app = new CISApplication(builder =>
        {
            ApplicationTestSupport.Configure(builder, inventory);
            builder.Services.RemoveAll<IHealthMonitorFactory>();
            builder.Services.RemoveAll<IHealthSnapshotSource>();
            builder.Services.RemoveAll<IHeartbeatOwnerFactory>();
        });
        try
        {
            await app.StartAsync().WaitAsync(ApplicationTestSupport.Deadline);
            Assert.IsNull(app.Services.GetService<IHealthMonitorFactory>());
            Assert.IsNull(app.Services.GetService<IHealthSnapshotSource>());
            Assert.IsNull(app.Services.GetService<IHeartbeatOwnerFactory>());
            Assert.IsNotNull(app.Services.GetService<HealthcheckService>());
            Assert.IsNotNull(app.Services.GetService<HeartbeatRuntime>());
        }
        finally
        {
            await app.StopAsync().WaitAsync(ApplicationTestSupport.Deadline);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DisabledOrRemovedModulesPreserveExplicitBusinessOverrides(bool disabled)
    {
        using var directory = new TemporaryDirectory();
        var inventory = ApplicationTestSupport.Inventory(directory);
        await using var health = new HealthcheckService(new HealthcheckOptions());
        await using var heartbeat = new HeartbeatRuntime(new HeartbeatOptions { MaxConcurrent = 1 });
        var factory = new SuppliedHealthFactory(health);
        var snapshots = new SuppliedSnapshotSource(health.CaptureSnapshot());
        var owners = new SuppliedOwnerFactory(heartbeat);
        await using var app = new CISApplication(builder =>
        {
            ApplicationTestSupport.Configure(builder, inventory);
            if (disabled)
            {
                builder.HealthcheckEnabled = false;
                builder.HeartbeatEnabled = false;
            }
            else
            {
                builder.Services.RemoveAll<HealthcheckService>();
                builder.Services.RemoveAll<HeartbeatRuntime>();
            }
            builder.Services.AddSingleton<IHealthMonitorFactory>(factory);
            builder.Services.AddSingleton<IHealthSnapshotSource>(snapshots);
            builder.Services.AddSingleton<IHeartbeatOwnerFactory>(owners);
        });
        try
        {
            await app.StartAsync().WaitAsync(ApplicationTestSupport.Deadline);
            Assert.AreSame(factory, app.Services.GetRequiredService<IHealthMonitorFactory>());
            Assert.AreSame(snapshots, app.Services.GetRequiredService<IHealthSnapshotSource>());
            Assert.AreSame(owners, app.Services.GetRequiredService<IHeartbeatOwnerFactory>());
            Assert.IsNull(app.Services.GetService<HealthcheckService>());
            Assert.IsNull(app.Services.GetService<HeartbeatRuntime>());
        }
        finally
        {
            await app.StopAsync().WaitAsync(ApplicationTestSupport.Deadline);
        }
    }

    [TestMethod]
    public async Task LegacyHostingUsesAnInstanceAliasForAnAlreadyRegisteredBorrowedRuntime()
    {
        await using var heartbeat = new HeartbeatRuntime(new HeartbeatOptions { MaxConcurrent = 1 });
        HostApplicationBuilder builder = Host.CreateApplicationBuilder([]);
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(heartbeat);
        builder.Services.AddCisHeartbeat();
        IHost host = builder.Build();
        try
        {
            Assert.AreSame(heartbeat, host.Services.GetRequiredService<IHeartbeatOwnerFactory>());
        }
        finally
        {
            if (host is IAsyncDisposable asynchronous)
            {
                await asynchronous.DisposeAsync();
            }
            else
            {
                host.Dispose();
            }
        }

        // No host startup occurred, so a borrowed runtime remains open too.
        await using IHeartbeatOwner owner = ((IHeartbeatOwnerFactory)heartbeat).CreateOwner();
        await owner.CloseAsync();
    }

    private sealed class SuppliedHealthFactory(HealthcheckService health) : IHealthMonitorFactory
    {
        public IConditionMonitor CreateCondition(ConditionMonitorOptions options) => health.CreateCondition(options);
        public IConsecutiveFailureMonitor CreateConsecutiveFailures(ConsecutiveFailureMonitorOptions options)
            => health.CreateConsecutiveFailures(options);
        public INumericThresholdMonitor CreateNumericThreshold(NumericThresholdMonitorOptions options)
            => health.CreateNumericThreshold(options);
        public IAvailabilityMonitor CreateAvailability(AvailabilityMonitorOptions options) => health.CreateAvailability(options);
    }

    private sealed class SuppliedSnapshotSource(HealthSnapshot snapshot) : IHealthSnapshotSource
    {
        public HealthSnapshot CaptureSnapshot() => snapshot;
    }

    private sealed class SuppliedOwnerFactory(IHeartbeatOwnerFactory owners) : IHeartbeatOwnerFactory
    {
        public IHeartbeatOwner CreateOwner() => owners.CreateOwner();
    }
}

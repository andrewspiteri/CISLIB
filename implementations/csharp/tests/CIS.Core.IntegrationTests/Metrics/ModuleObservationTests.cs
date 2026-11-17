using CIS.Configuration;
using CIS.Logging;
using CIS.Logging.MicrosoftLogger;
using CIS.Metrics.OpenTelemetry;
using CIS.TestSupport;
using CIS.Tests.IntegrationTests.Configuration;
using CIS.Tests.IntegrationTests.Startup;
using CIS.TypeManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using global::OpenTelemetry.Metrics;

namespace CIS.Tests.IntegrationTests.Metrics;

[TestClass]
[DoNotParallelize]
public sealed class ModuleObservationTests
{
    [TestMethod]
    public async Task OneActualHostAutomaticallyObservesAllModuleOwnersWithoutReplayingBootstrapOrRebuildingItsProvider()
    {
        using var directory = new TemporaryDirectory();
        ITypeManager manager = await ConfigurationHostTestSupport.ManagerAsync(directory);
        TypeManagerPreparationReport report = manager.PreparationReport;
        await using var store = new ConfigurationHostStore();
        store.Set<ConfigurationHostSettings>("{\"Count\":41,\"Label\":\"PRIVATE configuration value\"}", "c41");
        await using ConfigurationRuntime configuration = await ConfigurationRuntime.PrepareAsync(manager, ConfigurationHostTestSupport.Options(store));
        var writer = new QuietWriter();
        CoreLogRuntime logging = ModuleObservationSupport.QuietLogging(writer);
        using var provider = new CoreLoggerProvider(logging);
        StartupHost? host = null;
        try
        {
            host = ModuleObservationSupport.Host(manager, configuration, provider, defaultCache: false);
            await host.StartAsync().WaitAsync(StartupHost.Deadline);
            using HttpClient client = HostingTestSupport.Client(host);
            MetricsCollectionContext context = host.App.Services.GetRequiredService<MetricsCollectionContext>();
            MeterProvider sdk = host.App.Services.GetRequiredService<MeterProvider>();
            Assert.AreEqual(19, context.DefinitionCount, "All six actual owners must bind the fixed default Core definitions.");
            Assert.IsNull(host.App.Services.GetService<CoreLogRuntime>(), "The discovered logging source is borrowed from its existing provider.");
            int reads = store.Reads;
            string before = await HostingTestSupport.ScrapeAsync(client);
            _ = await HostingTestSupport.ScrapeAsync(client);
            Assert.AreEqual(reads, store.Reads, "Collection must not reload prepared Configuration.");
            Assert.AreSame(report, manager.PreparationReport);
            Assert.AreSame(manager, host.App.Services.GetRequiredService<ITypeManager>());
            Assert.AreEqual((double)manager.Types.Count, ModuleObservationSupport.Sum(before, "cis_typemanager_types"));
            Assert.AreEqual(1d, ModuleObservationSupport.Sum(before, "cis_configuration_prepared"));
            Assert.IsTrue(before.Contains("cis_configuration_initialization_duration_seconds{", StringComparison.Ordinal));
            Assert.IsFalse(before.Contains("operation=\"initial_load\"", StringComparison.Ordinal), "Pre-DI loads are terminal snapshots, not replayed histogram events.");
            Assert.IsFalse(before.Contains("PRIVATE", StringComparison.Ordinal));
            double refreshes = ModuleObservationSupport.Sum(before, "cis_configuration_operation_duration_seconds_count");

            ConfigurationHostSettings settings = host.App.Services.GetRequiredService<ConfigurationHostSettings>();
            IConfigurationReader reader = host.App.Services.GetRequiredService<IConfigurationReader>();
            var changed = ConfigurationHostTestSupport.Signal();
            settings.ConfigurationChanged += (_, _) => changed.TrySetResult();
            store.Set<ConfigurationHostSettings>("{\"Count\":42,\"Label\":\"PRIVATE newer value\"}", "c42");
            store.Signal();
            await changed.Task.WaitAsync(StartupHost.Deadline);
            Assert.AreEqual(42, reader.Read(settings, current => current.Count));
            Assert.AreSame(settings, host.App.Services.GetRequiredService<ConfigurationHostSettings>());
            // Notification publication precedes the duration's final settlement.
            // Await that observable fact using real scrapes within one finite budget.
            using var settled = new CancellationTokenSource(StartupHost.Deadline);
            string after;
            do
            {
                settled.Token.ThrowIfCancellationRequested();
                after = await HostingTestSupport.ScrapeAsync(client).WaitAsync(settled.Token);
            } while (ModuleObservationSupport.Sum(after, "cis_configuration_operation_duration_seconds_count") <= refreshes);
            Assert.AreSame(context, host.App.Services.GetRequiredService<MetricsCollectionContext>());
            Assert.AreSame(sdk, host.App.Services.GetRequiredService<MeterProvider>());
            Assert.IsFalse(after.Contains("PRIVATE", StringComparison.Ordinal));

            double front = ModuleObservationSupport.Sum(after, "cis_logging_records_total");
            double back = ModuleObservationSupport.Sum(after, "cis_logging_processing_records_total");
            provider.CreateLogger("ModuleObservation").LogInformation(new EventId(71, "metrics.module.probe"), "PRIVATE log content");
            Assert.IsTrue((await logging.FlushAsync(StartupHost.Deadline, CancellationToken.None)).Succeeded);
            string logged = await HostingTestSupport.ScrapeAsync(client);
            Assert.AreEqual(front + 1, ModuleObservationSupport.Sum(logged, "cis_logging_records_total"));
            Assert.AreEqual(back + 1, ModuleObservationSupport.Sum(logged, "cis_logging_processing_records_total"));
            Assert.AreEqual(1, writer.Writes);
            Assert.IsFalse(logged.Contains("PRIVATE", StringComparison.Ordinal));
        }
        finally
        {
            if (host is not null) { await host.StopAsync(CancellationToken.None).WaitAsync(StartupHost.Deadline); }
            await ModuleObservationSupport.StopLoggingAsync(logging);
            if (host is not null) { await host.DisposeAsync(); }
            await writer.DisposeAsync();
        }
        Assert.AreEqual(1, store.WatchDisposals);
    }
}

using CIS.CommonTesting;
using CIS.Metrics;
using CIS.Metrics.OpenTelemetry;
using CIS.TestSupport.Transactions;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using CIS.Transactions.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Metrics;

[TestClass]
public sealed class TransactionCollectionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FinalPassiveCompositionReservesThreeStreamsInEitherRegistrationOrder(bool metricsFirst)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        if (metricsFirst)
        {
            builder.Services.AddCisMetrics();
        }
        for (int index = 0; index < 16; index++)
        {
            builder.Services.AddCisAdoNetDatabase("database" + index,
                _ => throw new AssertFailedException("Catalog and attachment cannot resolve a native source."), new()
                {
                    DefaultIsolation = TransactionIsolation.RepeatableRead,
                    SupportedIsolationLevels = [TransactionIsolation.ReadCommitted, TransactionIsolation.RepeatableRead]
                });
        }
        if (!metricsFirst)
        {
            builder.Services.AddCisMetrics();
        }
        IHost host = builder.Build();
        try
        {
            MetricsCollectionContext context = host.Services.GetRequiredService<MetricsCollectionContext>();
            Assert.AreEqual(24, context.CaptureCatalog().Count);
            Assert.AreEqual(3456, context.CaptureCatalog().Sum(stream => stream.CardinalityLimit));
            Assert.AreEqual(0, context.DefinitionCount);
            Assert.AreEqual(16, host.Services.GetRequiredService<TransactionsComposition>().Databases.Count);
            await host.StartAsync().WaitAsync(CollectionTestHost.Deadline);
            Assert.IsTrue(context.Settings.CollectTransactions);
            Assert.AreEqual(3, context.DefinitionCount);
            AssertStream("cis.transaction.duration", 48, ["database", "outcome"]);
            AssertStream("cis.transaction.active", 16, ["database"]);
            AssertStream("cis.transaction.failures", 64, ["database", "phase"]);
            CollectionAssert.AreEqual(MetricsStreamCatalog.DurationBounds.ToArray(), context.CaptureCatalog()
                .Single(stream => stream.Name == "cis.transaction.duration").HistogramBounds.ToArray());

            void AssertStream(string name, int capacity, string[] keys)
            {
                MetricsStreamSnapshot stream = context.CaptureCatalog().Single(item => item.Name == name);
                Assert.AreEqual(capacity, stream.CardinalityLimit);
                CollectionAssert.AreEqual(keys, stream.DimensionKeys.ToArray());
                Assert.IsTrue(stream.Selected);
            }
        }
        finally
        {
            await StopAndDispose(host);
        }
    }

    [TestMethod]
    [DataRow("cis.transaction.duration", 47)]
    [DataRow("cis.transaction.active", 15)]
    [DataRow("cis.transaction.failures", 63)]
    public async Task UndersizedTransactionOverridesRejectTheKnownProfileBeforeActivation(string name, int capacity)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddCisMetrics(options => options.Streams.Add(name, new() { CardinalityLimit = capacity }));
        builder.Services.AddCisTransactions();
        IHost host = builder.Build();
        try
        {
            Assert.ThrowsExactly<ArgumentException>(() => host.Services.GetRequiredService<MetricsCollectionContext>());
        }
        finally
        {
            await StopAndDispose(host);
        }
    }

    [TestMethod]
    [DataRow("no_collector")]
    [DataRow("disabled")]
    [DataRow("core_opt_out")]
    public async Task UnselectedCollectionLeavesTransactionsUsableAndAddsNoTransactionDefinitions(string selection)
    {
        using var source = new RecordingTransactionDataSource();
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddCisAdoNetDatabase("Ledger", _ => source,
            new()
            {
                SupportedIsolationLevels = [TransactionIsolation.ReadCommitted]
            });
        if (selection != "no_collector")
        {
            builder.Services.AddCisMetrics(options =>
            {
                options.Enabled = selection != "disabled";
                options.CollectCoreModules = selection != "core_opt_out";
            });
        }
        IHost host = builder.Build();
        try
        {
            await host.StartAsync().WaitAsync(CollectionTestHost.Deadline);
            await using ILocalTransaction owner = await host.Services.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");
            Assert.AreEqual(TransactionOutcome.Committed, (await owner.CommitAsync()).Outcome);
            MetricsCollectionContext? collection = host.Services.GetService<MetricsCollectionContext>();
            if (selection == "core_opt_out")
            {
                Assert.IsNotNull(collection);
                Assert.IsFalse(collection.Settings.CollectTransactions);
                Assert.AreEqual(0, collection.DefinitionCount);
                Assert.IsFalse(collection.CaptureCatalog().Any(stream => stream.Name.StartsWith("cis.transaction.", StringComparison.Ordinal)));
            }
            else
            {
                Assert.IsNull(collection);
            }
        }
        finally
        {
            await StopAndDispose(host);
        }
    }

    [TestMethod]
    public async Task ASubstitutedInstrumentationOwnerCannotBindAnotherRegistrationsComposition()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddCisTransactions();
        builder.Services.AddCisMetrics();
        builder.Services.RemoveAll<TransactionInstrumentation>();
        builder.Services.AddSingleton(new TransactionInstrumentation(new TransactionsComposition(new object(), [])));
        IHost host = builder.Build();
        try
        {
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                host.StartAsync().WaitAsync(CollectionTestHost.Deadline));
            Assert.IsTrue(error.Message.Contains("exact registered instrumentation owner", StringComparison.Ordinal));
        }
        finally
        {
            await StopAndDispose(host);
        }
    }

    [TestMethod]
    public async Task ActualScrapeUsesTheReportDurationAfterHeldCleanupAndKeepsMandatoryIdentity()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-11-20T00:00:00Z"));
        var cleanupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var source = new RecordingTransactionDataSource
        {
            ConnectionCreated = connection =>
            {
                clock.Advance(TimeSpan.FromSeconds(10));
                connection.DisposeHandler = () =>
                {
                    clock.Advance(TimeSpan.FromSeconds(0.25));
                    return ValueTask.CompletedTask;
                };
                connection.TransactionCreated = transaction =>
                {
                    clock.Advance(TimeSpan.FromSeconds(20));
                    transaction.CommitHandler = _ =>
                    {
                        clock.Advance(TimeSpan.FromSeconds(0.5));
                        return Task.CompletedTask;
                    };
                    transaction.DisposeHandler = async () =>
                    {
                        clock.Advance(TimeSpan.FromSeconds(1));
                        cleanupEntered.TrySetResult();
                        await releaseCleanup.Task;
                    };
                };
            }
        };
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(TransactionCollectionTests).Assembly.FullName,
            EnvironmentName = "TransactionCollection"
        });
        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = true;
        });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddCisMetrics(options => options.ScrapeCacheDuration = TimeSpan.Zero);
        builder.Services.AddCisAdoNetDatabase("Ledger", _ => source,
            new()
            {
                SupportedIsolationLevels = [TransactionIsolation.ReadCommitted]
            });
        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCisMetrics();
        ILocalTransaction? owner = null;
        Task<TransactionCompletion>? completion = null;
        try
        {
            await app.StartAsync().WaitAsync(CollectionTestHost.Deadline);
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = CollectionTestHost.Deadline };
            owner = await app.Services.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");
            bool businessValue = await owner.Context.UseAdoNetAsync((_, _, _) =>
            {
                clock.Advance(TimeSpan.FromSeconds(2));
                return Task.FromResult(false);
            });
            Assert.IsFalse(businessValue);
            completion = owner.CommitAsync();
            await cleanupEntered.Task.WaitAsync(CollectionTestHost.Deadline);
            Assert.IsFalse(completion.IsCompleted);
            Assert.IsNull(owner.Completion);
            string pending = await Scrape(client);
            CollectionText.AssertIdentity(pending);
            Assert.AreEqual(1d, Sum(pending, "cis_transaction_active{"));
            Assert.AreEqual(0d, Sum(pending, "cis_transaction_duration_seconds_count{"));

            releaseCleanup.TrySetResult();
            TransactionCompletion result = await completion.WaitAsync(CollectionTestHost.Deadline);
            Assert.AreEqual(TransactionOutcome.Committed, result.Outcome);
            Assert.AreEqual(3.75, result.DurationSeconds);
            string finished = await Scrape(client);
            CollectionText.AssertIdentity(finished);
            Assert.AreEqual(result.DurationSeconds!.Value, Sum(finished, "cis_transaction_duration_seconds_sum{"));
            Assert.AreEqual(1d, Sum(finished, "cis_transaction_duration_seconds_count{"));
            Assert.AreEqual(0d, Sum(finished, "cis_transaction_active{"));
            Assert.AreEqual(0d, Sum(finished, "cis_transaction_failures_total{"));
            Assert.IsTrue(finished.Contains("outcome=\"committed\"", StringComparison.Ordinal));
            Assert.IsFalse(finished.Contains("fixture-private-connection", StringComparison.Ordinal));
            string[] buckets = CollectionText.Samples(finished).Where(line =>
                line.StartsWith("cis_transaction_duration_seconds_bucket{", StringComparison.Ordinal)
                && line.Contains("database=\"Ledger\"", StringComparison.Ordinal)).ToArray();
            Assert.HasCount(15, buckets, "The actual exported duration keeps fourteen finite bounds plus infinity.");
        }
        finally
        {
            releaseCleanup.TrySetResult();
            try
            {
                if (completion is not null)
                {
                    await completion;
                }
                if (owner is not null)
                {
                    await owner.DisposeAsync();
                }
            }
            finally
            {
                try
                {
                    await app.StopAsync(CancellationToken.None).WaitAsync(CollectionTestHost.Deadline);
                }
                finally
                {
                    await app.DisposeAsync();
                }
            }
        }

        static double Sum(string text, string prefix) => CollectionText.Samples(text).Where(line =>
            line.StartsWith(prefix, StringComparison.Ordinal) && line.Contains("database=\"Ledger\"", StringComparison.Ordinal))
            .Sum(CollectionText.Value);
    }

    private static async Task<string> Scrape(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync("/_metrics").WaitAsync(CollectionTestHost.Deadline);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task StopAndDispose(IHost host)
    {
        try
        {
            await host.StopAsync(CancellationToken.None).WaitAsync(CollectionTestHost.Deadline);
        }
        finally
        {
            if (host is IAsyncDisposable asyncHost)
            {
                await asyncHost.DisposeAsync();
            }
            else
            {
                host.Dispose();
            }
        }
    }
}

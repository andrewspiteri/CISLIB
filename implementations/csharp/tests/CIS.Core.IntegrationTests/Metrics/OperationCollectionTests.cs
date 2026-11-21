using System.Text;
using CIS.CommonTesting;
using CIS.Metrics.OpenTelemetry;
using CIS.Operations;
using CIS.Operations.Hosting;
using CIS.Security;
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
public sealed class OperationCollectionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FinalCompositionReservesExactlyTwoFiniteStreamsInEitherOrderWithoutResolvingBusinessServices(bool metricsFirst)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        if (metricsFirst)
        {
            builder.Services.AddCisMetrics();
        }
        builder.Services.AddCisTransactions();
        for (int index = 0; index < 16; index++)
        {
            builder.Services.AddCisOperation(Definition("operation" + index));
        }
        if (!metricsFirst)
        {
            builder.Services.AddCisMetrics();
        }
        IHost host = builder.Build();
        try
        {
            MetricsCollectionContext context = host.Services.GetRequiredService<MetricsCollectionContext>();
            Assert.AreEqual(26, context.CaptureCatalog().Count);
            Assert.AreEqual(3808, context.CaptureCatalog().Sum(stream => stream.CardinalityLimit));
            Assert.AreEqual(0, context.DefinitionCount);
            await host.StartAsync().WaitAsync(CollectionTestHost.Deadline);
            Assert.IsTrue(context.Settings.CollectOperations);
            Assert.AreEqual(5, context.DefinitionCount, "Only two operation and three transaction definitions activate.");
            AssertStream("cis.operation.execute.duration", 208);
            AssertStream("cis.operation.lookup.duration", 144);

            void AssertStream(string name, int capacity)
            {
                MetricsStreamSnapshot stream = context.CaptureCatalog().Single(item => item.Name == name);
                Assert.AreEqual(capacity, stream.CardinalityLimit);
                Assert.IsTrue(stream.Selected);
                CollectionAssert.AreEqual(new[] { "operation", "outcome" }, stream.DimensionKeys.ToArray());
                CollectionAssert.AreEqual(MetricsStreamCatalog.DurationBounds.ToArray(), stream.HistogramBounds.ToArray());
            }
        }
        finally
        {
            await StopAndDispose(host);
        }
    }

    [TestMethod]
    [DataRow("cis.operation.execute.duration", 207)]
    [DataRow("cis.operation.lookup.duration", 143)]
    [DataRow("max_streams", 25)]
    [DataRow("total_capacity", 3807)]
    public async Task ExplicitProfilesMustCoverTheKnownOperationAndTransactionCatalog(string selection, int capacity)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddCisMetrics(options =>
        {
            if (selection == "max_streams")
            {
                options.MaxDefinitions = 25;
                options.MaxStreams = capacity;
            }
            else if (selection == "total_capacity")
            {
                options.TotalCardinalityLimit = capacity;
            }
            else
            {
                options.Streams.Add(selection, new()
                {
                    CardinalityLimit = capacity
                });
            }
        });
        builder.Services.AddCisTransactions();
        builder.Services.AddCisOperation(Definition("Debit"));
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
    public async Task UnselectedCollectionKeepsOperationsUsableWithoutOperationDefinitions(string selection)
    {
        using var source = new RecordingTransactionDataSource();
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        RegisterRuntime(builder.Services, source, new CollectionJournal());
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
            using IServiceScope scope = host.Services.CreateScope();
            await using ILocalTransaction owner = await host.Services.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");
            Assert.AreEqual(OperationExecutionDisposition.Prepared, (await scope.ServiceProvider.GetRequiredService<IDurableOperationService>()
                .ExecuteAsync(owner.Context, new OperationRequest<int>("Debit", "private-scope", "private-key", 10), Success)).Disposition);
            MetricsCollectionContext? context = host.Services.GetService<MetricsCollectionContext>();
            if (context is not null)
            {
                Assert.IsFalse(context.Settings.CollectOperations);
                Assert.IsFalse(context.CaptureCatalog().Any(item => item.Name.StartsWith("cis.operation.", StringComparison.Ordinal)));
                Assert.AreEqual(0, context.DefinitionCount);
            }
            else
            {
                Assert.AreNotEqual("core_opt_out", selection);
            }
        }
        finally
        {
            await StopAndDispose(host);
        }
    }

    [TestMethod]
    public async Task AForeignInstrumentationOwnerCannotAttachAnotherComposition()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddCisOperation(Definition("Debit"));
        builder.Services.AddCisMetrics();
        builder.Services.RemoveAll<OperationInstrumentation>();
        builder.Services.AddSingleton(new OperationInstrumentation(new OperationsComposition(new object(), ["Debit"])));
        IHost host = builder.Build();
        try
        {
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync().WaitAsync(CollectionTestHost.Deadline));
            Assert.IsTrue(error.Message.Contains("exact registered instrumentation owner", StringComparison.Ordinal));
        }
        finally
        {
            await StopAndDispose(host);
        }
    }

    [TestMethod]
    public async Task ActualExporterKeepsIdentityPreparationMeaningCleanupDurationAndHostIsolation()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool holdCleanup = false;
        using var source = new RecordingTransactionDataSource
        {
            ConnectionCreated = connection => connection.DisposeHandler = async () =>
            {
                if (holdCleanup)
                {
                    clock.Advance(TimeSpan.FromSeconds(0.75));
                    entered.TrySetResult();
                    await release.Task;
                }
            }
        };
        var journal = new CollectionJournal { OnRead = () => clock.Advance(TimeSpan.FromSeconds(0.5)) };
        await using WebApplication first = CreateWeb(source, journal, clock);
        using var secondSource = new RecordingTransactionDataSource();
        await using WebApplication second = CreateWeb(secondSource, new CollectionJournal());
        Task<OperationLookup<string>>? lookup = null;
        try
        {
            await first.StartAsync().WaitAsync(CollectionTestHost.Deadline);
            await second.StartAsync().WaitAsync(CollectionTestHost.Deadline);
            using var firstClient = new HttpClient { BaseAddress = new(first.Urls.Single()), Timeout = CollectionTestHost.Deadline };
            using var secondClient = new HttpClient { BaseAddress = new(second.Urls.Single()), Timeout = CollectionTestHost.Deadline };
            using IServiceScope scope = first.Services.CreateScope();
            IDurableOperationService service = scope.ServiceProvider.GetRequiredService<IDurableOperationService>();
            await using (ILocalTransaction owner = await first.Services.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger"))
            {
                OperationExecution<string> prepared = await service.ExecuteAsync(owner.Context,
                    new OperationRequest<int>("Debit", "private-scope", "private-key", 10), (_, _, _) =>
                    {
                        clock.Advance(TimeSpan.FromSeconds(1.25));
                        return Task.FromResult(OperationDecision<string>.Succeed("private-result"));
                    });
                Assert.AreEqual(OperationExecutionDisposition.Prepared, prepared.Disposition);
                Assert.IsNull(owner.Completion);
                string preparedText = await Scrape(firstClient);
                CollectionText.AssertIdentity(preparedText);
                Assert.AreEqual(1d, Sum(preparedText, "cis_operation_execute_duration_seconds_count{"));
                Assert.AreEqual(1.25d, Sum(preparedText, "cis_operation_execute_duration_seconds_sum{"));
                Assert.IsTrue(preparedText.Contains("outcome=\"prepared_succeeded\"", StringComparison.Ordinal));
                Assert.AreEqual(0d, Sum(preparedText, "cis_transaction_duration_seconds_count{"), "Prepared cannot invent a committed transaction.");
                await owner.CommitAsync();
            }
            holdCleanup = true;
            lookup = service.GetResultAsync<string>(new("Debit", "private-scope", "private-key"));
            await entered.Task.WaitAsync(CollectionTestHost.Deadline);
            Assert.IsFalse(lookup.IsCompleted);
            string pending = await Scrape(firstClient);
            Assert.AreEqual(0d, Sum(pending, "cis_operation_lookup_duration_seconds_count{"));
            release.TrySetResult();
            Assert.AreEqual(OperationLookupDisposition.Found, (await lookup).Disposition);
            string finished = await Scrape(firstClient);
            CollectionText.AssertIdentity(finished);
            Assert.AreEqual(1d, Sum(finished, "cis_operation_lookup_duration_seconds_count{"));
            Assert.AreEqual(1.25d, Sum(finished, "cis_operation_lookup_duration_seconds_sum{"));
            Assert.IsTrue(finished.Contains("outcome=\"found_succeeded\"", StringComparison.Ordinal));
            Assert.IsFalse(finished.Contains("private-", StringComparison.Ordinal));
            Assert.HasCount(15, CollectionText.Samples(finished).Where(line => line.StartsWith("cis_operation_execute_duration_seconds_bucket{", StringComparison.Ordinal)).ToArray());
            string other = await Scrape(secondClient);
            CollectionText.AssertIdentity(other);
            Assert.AreEqual(0d, Sum(other, "cis_operation_execute_duration_seconds_count{"));
            Assert.AreEqual(0d, Sum(other, "cis_operation_lookup_duration_seconds_count{"));
        }
        finally
        {
            release.TrySetResult();
            if (lookup is not null)
            {
                await lookup;
            }
            await first.StopAsync(CancellationToken.None).WaitAsync(CollectionTestHost.Deadline);
            await second.StopAsync(CancellationToken.None).WaitAsync(CollectionTestHost.Deadline);
        }
    }

    private static OperationDefinition<int, string> Definition(string name) => new(name, "Ledger", new("collection-request", 1),
        new CollectionNormalizer(), new CollectionCodec(), _ => new CollectionResolver(), new()
        {
            ExecuteCapabilities = ["ledger.submit", "ledger.read"],
            LookupCapabilities = ["ledger.read"]
        });

    private static void RegisterRuntime(IServiceCollection services, RecordingTransactionDataSource source, CollectionJournal journal)
    {
        services.AddCisAdoNetDatabase("Ledger", _ => source, new()
        {
            SupportedIsolationLevels = [TransactionIsolation.ReadCommitted]
        });
        services.AddSingleton<IOperationJournal>(journal);
        services.AddSingleton<ICapabilityAuthorizer, CollectionAuthorizer>();
        services.AddSingleton<ICallerContextAccessor, CollectionCaller>();
        services.AddCisOperation(Definition("Debit"));
        services.AddCisOperations();
    }

    private static WebApplication CreateWeb(RecordingTransactionDataSource source, CollectionJournal journal, TimeProvider? clock = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(OperationCollectionTests).Assembly.FullName,
            EnvironmentName = "OperationCollection"
        });
        builder.Host.UseDefaultServiceProvider(options => { options.ValidateScopes = true; options.ValidateOnBuild = true; });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        if (clock is not null)
        {
            builder.Services.AddSingleton(clock);
        }
        builder.Services.AddCisMetrics(options => options.ScrapeCacheDuration = TimeSpan.Zero);
        RegisterRuntime(builder.Services, source, journal);
        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCisMetrics();
        return app;
    }

    private static Task<OperationDecision<string>> Success(ILocalTransactionContext _, int input, CancellationToken token)
        => Task.FromResult(OperationDecision<string>.Succeed(input.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private static async Task<string> Scrape(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync("/_metrics").WaitAsync(CollectionTestHost.Deadline);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static double Sum(string text, string prefix) => CollectionText.Samples(text)
        .Where(line => line.StartsWith(prefix, StringComparison.Ordinal)).Sum(CollectionText.Value);

    private static async Task StopAndDispose(IHost host)
    {
        try
        {
            await host.StopAsync(CancellationToken.None).WaitAsync(CollectionTestHost.Deadline);
        }
        finally
        {
            if (host is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }
            else
            {
                host.Dispose();
            }
        }
    }

    private sealed class CollectionNormalizer : IOperationRequestNormalizer<int>
    {
        public NormalizedOperationInput<int> Normalize(int input, OperationScope scope)
            => new(input, [new(new("collection-request", 1), OperationCanonicalValue.Integer(input))]);
    }

    private sealed class CollectionCodec : IOperationResultCodec<string>
    {
        public OperationContract CurrentContract => new("collection-result", 1);
        public IReadOnlyList<OperationContract> ReadableContracts => [CurrentContract];
        public byte[] Encode(string? value) => Encoding.UTF8.GetBytes(value ?? "");
        public bool TryDecode(OperationContract contract, ReadOnlyMemory<byte> value, out string? decoded)
        {
            decoded = Encoding.UTF8.GetString(value.Span);
            return true;
        }
    }

    private sealed class CollectionResolver : IOperationScopeResolver
    {
        public ValueTask<OperationScope> ResolveAsync(string operationName, string requestedScope, CallerContext? caller,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(new OperationScope("trusted-scope", "account", "actual-account"));
    }

    private sealed class CollectionAuthorizer : ICapabilityAuthorizer
    {
        public ValueTask<AuthorizationDecision> AuthorizeAsync(IReadOnlyList<string> capabilities, CapabilityTarget target,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(AuthorizationDecision.Allow);
    }

    private sealed class CollectionCaller : ICallerContextAccessor
    {
        public CallerContext? Current => null;
    }

    // Only supplies controlled orchestration facts to qualify SDK collection, never SQL durability.
    private sealed class CollectionJournal : IOperationJournal
    {
        private OperationFingerprint? fingerprint;
        private OperationStoredResult? result;
        internal Action? OnRead;
        public Task<OperationClaim> TryClaimAsync(ILocalTransactionContext context, OperationIdentity identity,
            OperationFingerprint fingerprint, CancellationToken cancellationToken = default)
            => context.UseAdoNetAsync((_, _, _) => Task.FromResult(new OperationClaim(OperationClaimDisposition.Acquired)), cancellationToken);
        public Task<OperationJournalRecord?> ReadAsync(ILocalTransactionContext context, OperationIdentity identity,
            CancellationToken cancellationToken = default)
        {
            OnRead?.Invoke();
            return context.UseAdoNetAsync<OperationJournalRecord?>((_, _, _) => Task.FromResult<OperationJournalRecord?>(
                result is null ? null : new(identity, fingerprint, result, new(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), true)), cancellationToken);
        }
        public async Task<OperationJournalTimes> WriteCompletedAsync(ILocalTransactionContext context, OperationIdentity identity,
            OperationFingerprint fingerprint, OperationStoredResult result, OperationRetentionPolicy retention,
            CancellationToken cancellationToken = default)
        {
            await context.UseAdoNetAsync((_, _, _) => Task.CompletedTask, cancellationToken);
            this.fingerprint = fingerprint;
            this.result = result;
            return new(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        }
    }
}

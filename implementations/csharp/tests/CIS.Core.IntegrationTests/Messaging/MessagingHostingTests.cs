using System.Data.Common;
using System.Text.Json;
using System.Text.Encodings.Web;
using CIS.Hosting;
using CIS.Messaging;
using CIS.Messaging.Hosting;
using CIS.Messaging.RabbitMQ;
using CIS.Metrics.OpenTelemetry;
using CIS.Security;
using CIS.Security.Hosting;
using CIS.Startup;
using CIS.Startup.Hosting;
using CIS.TestSupport;
using CIS.Tests.IntegrationTests.Application;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using CIS.Transactions.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Messaging;

[TestClass]
[DoNotParallelize]
public sealed class MessagingHostingTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ActualNativeDisconnectUsesTheConfiguredRequestReadinessPolicy(bool requireLiveBroker)
    {
        await using PostgresMessageFixture database = await PostgresMessageFixture.CreateAsync();
        await using RabbitMqMessageFixture broker = await RabbitMqMessageFixture.CreateAsync(
            withSubscription: false, withRequests: true, transformSnapshot: original =>
                new(new MessagingOptions { RequireLiveBrokerForReadiness = requireLiveBroker },
                    original.Routes, [new("Messaging", "Messaging")], [], original.Requests));
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        RegisterNative(builder.Services, broker, database);
        builder.Services.AddCisMessaging();
        builder.Services.AddCisStartup();
        IHost host = builder.Build();
        try
        {
            await host.StartAsync().WaitAsync(TimeSpan.FromSeconds(30));
            var runtime = host.Services.GetRequiredService<MessagingRuntime>();
            var transport = host.Services.GetRequiredService<RabbitMqTransport>();
            Assert.IsTrue(transport.IsReady);
            Assert.IsTrue(runtime.IsReady);
            MessageReplyRoute? endpoint = transport.ReplyEndpoint;
            Assert.IsNotNull(endpoint);
            broker.TrackReplyEndpoint(endpoint.EndpointId);

            // Close this root's actual native connection while its runtime and SQL store stay live.
            // This establishes the readiness policy after real disconnection, not automatic recovery.
            await transport.StopAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.IsFalse(transport.IsReady);
            Assert.AreEqual(!requireLiveBroker, runtime.IsReady);
            Assert.AreEqual(0L, await database.CountAsync("outbox"));
            Assert.AreEqual(0L, await database.CountAsync("processing"));
        }
        finally
        {
            try
            {
                await host.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
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
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FacadeUsesActualStoreAndBrokerThenDrainsTransactionsAndScopesBeforeDependenciesAndRecoversUnfinishedWork(bool web)
    {
        await using PostgresMessageFixture database = await PostgresMessageFixture.CreateAsync();
        var signals = new HeldWork();
        var codec = new NumberCodec();
        await using RabbitMqMessageFixture broker = await RabbitMqMessageFixture.CreateAsync(transformSnapshot: original =>
            new(original.Settings, original.Routes, [new("Messaging", "Messaging")],
                [new MessageSubscriptionDefinition<int>("A", "Messaging", "events", codec,
                    services => services.GetRequiredService<HostedHandler>(),
                    (_, _, _, _) => ValueTask.FromResult(new CapabilityTarget(PostgresMessageFixture.Application, PostgresMessageFixture.EnvironmentName)),
                    ["fixture.handle"])], []));
        using var directory = new TemporaryDirectory();
        var inventory = ApplicationTestSupport.Inventory(directory);
        var business = new ApplicationTestSupport.Dependency();
        int initialized = 0;
        CISApplication Create(ApplicationTestSupport.Dependency dependency)
        {
            void Configure(CISApplicationBuilder setup)
            {
                ApplicationTestSupport.Configure(setup, inventory);
                setup.Services.AddSingleton(_ => dependency);
                setup.Services.AddSingleton<IHostedService>(root => root.GetRequiredService<ApplicationTestSupport.Dependency>());
                setup.Services.AddSingleton<IStartable>(new ApplicationTestSupport.Participant(_ =>
                {
                    Interlocked.Increment(ref initialized);
                    return Task.CompletedTask;
                }));
                setup.Services.AddScoped(_ => new HostedHandler(database, signals, () =>
                {
                    Assert.IsTrue(Volatile.Read(ref initialized) > 0, "Business startup must precede the first native consumer invocation.");
                    dependency.Use();
                }));
                RegisterNative(setup.Services, broker, database);
                setup.Services.AddCisMetrics(options => options.CollectIncomingHttp = false);
                setup.UseMessaging();
            }
            return web ? CISApplication.CreateWeb([], Configure, _ => { }) : new CISApplication(Configure);
        }

        CISApplication app = Create(business);
        Task? disposal = null;
        try
        {
            await app.StartAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.IsTrue(app.Services.GetRequiredService<MessagingRuntime>().IsReady);
            MessageSubmission submitted = await app.Services.GetRequiredService<IMessagingClient>().PublishAsync("events", 7);
            Assert.AreEqual(MessageSubmissionOutcome.OutboxCommitted, submitted.Outcome);
            await signals.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            using var cancelledWait = new CancellationTokenSource();
            Task wait = app.StopAsync(cancelledWait.Token);
            cancelledWait.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => wait);
            disposal = app.DisposeAsync().AsTask();
            await signals.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsFalse(disposal.IsCompleted);
            Assert.IsFalse(business.Stopping.Task.IsCompleted);
            business.Use();
            signals.Release.TrySetResult();
            await signals.ScopeCleanup.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsFalse(disposal.IsCompleted, "An ended transaction does not substitute for actual independent scope cleanup.");
            Assert.IsFalse(business.Stopping.Task.IsCompleted);
            Assert.AreEqual(0L, await database.CountAsync("business_record"));
            signals.ReleaseScope.TrySetResult();
            await disposal.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.IsTrue(business.Stopping.Task.IsCompleted);
            Assert.AreEqual(1, business.Disposals);
            Assert.AreEqual(0L, await database.CountAsync("processing"));
            await RabbitMqMessageFixture.UntilAsync(async () => await broker.TotalQueuedAsync(broker.Queue) > 0,
                TimeSpan.FromSeconds(15));
        }
        finally
        {
            signals.Release.TrySetResult();
            signals.ReleaseScope.TrySetResult();
            await (disposal ?? app.DisposeAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(30));
        }

        var recoveredBusiness = new ApplicationTestSupport.Dependency();
        await using CISApplication recovered = Create(recoveredBusiness);
        await recovered.StartAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await RabbitMqMessageFixture.UntilAsync(async () => await database.CountAsync("business_record") == 1
            && await broker.TotalQueuedAsync(broker.Queue) == 0, TimeSpan.FromSeconds(30));
        Assert.AreEqual(1L, await database.CountAsync("processing"));
        Assert.AreEqual(2, signals.Calls);
        Assert.IsTrue(recovered.Services.GetRequiredService<MessagingRuntime>().IsReady);
        await recovered.StopAsync().WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static void RegisterNative(IServiceCollection services, RabbitMqMessageFixture broker, PostgresMessageFixture database)
    {
        services.AddSingleton(broker.Snapshot);
        services.AddCisAdoNetDatabase("Messaging", _ => database.Boundary,
            new()
            {
                SupportedIsolationLevels = [TransactionIsolation.ReadCommitted]
            });
        services.AddSingleton<IMessagingStore>(database.Store);
        RabbitMqSettings selected = broker.Options.Capture();
        var security = new SecurityOptions { PolicyVersion = "rm04-host" };
        SecuritySubject subject = new(selected.SubjectIssuer, broker.ServiceUser);
        security.Profiles.Add(new(selected.SecurityProfile, SecurityProfileKind.Native, SecurityProfileRoles.Inbound,
            "cis-rabbitmq", [new(subject, broker.Local.Application, broker.Local.Environment)]));
        security.Capabilities.Add("fixture.handle");
        security.Grants.Add(new(selected.SecurityProfile, "fixture.handle", PostgresMessageFixture.Application,
            PostgresMessageFixture.EnvironmentName, subject: subject));
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, BrokerOnlyScheme>("cis-rabbitmq", _ => { });
        services.AddCisSecurity(security);
        services.AddCisRabbitMqMessaging(broker.Options);
    }

    private sealed class HeldWork
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ScopeCleanup = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseScope = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls;
        internal int Disposals;
    }

    // The selected named native scheme is registered for Security startup validation.
    // HTTP itself supplies no broker identity; only the adapter's private delivery tickets authenticate.
    private sealed class BrokerOnlyScheme(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }

    private sealed class HostedHandler(PostgresMessageFixture database, HeldWork signals, Action dependency) : IMessageHandler<int>, IAsyncDisposable
    {
        public async Task HandleAsync(MessageHandlerContext context, int value, CancellationToken cancellationToken = default)
        {
            dependency();
            int call = Interlocked.Increment(ref signals.Calls);
            await database.InsertBusinessAsync(context.Transaction, context.Envelope.MessageId, "A");
            if (call == 1)
            {
                using CancellationTokenRegistration cancellation = cancellationToken.UnsafeRegister(
                    static state => ((HeldWork)state!).Cancelled.TrySetResult(), signals);
                signals.Entered.TrySetResult();
                await signals.Release.Task;
                dependency();
                await context.Transaction.UseAdoNetAsync(async (connection, transaction, token) =>
                {
                    await using DbCommand command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = "SELECT 1";
                    Assert.AreEqual(1, await command.ExecuteScalarAsync(token));
                });
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Increment(ref signals.Disposals) == 1)
            {
                signals.ScopeCleanup.TrySetResult();
                await signals.ReleaseScope.Task;
            }
            dependency();
        }
    }

    private sealed class NumberCodec : IMessageCodec<int>
    {
        public MessageContract Contract { get; } = new("fixture.number", 1);
        public byte[] Encode(int value) => JsonSerializer.SerializeToUtf8Bytes(value);
        public bool TryDecode(MessageContract contract, ReadOnlyMemory<byte> json, out int value)
        {
            value = JsonSerializer.Deserialize<int>(json.Span);
            return contract == Contract;
        }
    }
}

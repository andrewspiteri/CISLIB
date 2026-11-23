using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CIS.Audit;
using CIS.Audit.Hosting;
using CIS.Messaging;
using CIS.Messaging.Hosting;
using CIS.Messaging.RabbitMQ;
using CIS.Operations;
using CIS.Operations.Hosting;
using CIS.Operations.PostgreSql;
using CIS.Security;
using CIS.Security.Hosting;
using CIS.Tests.IntegrationTests.Messaging;
using CIS.Tests.IntegrationTests.Operations;
using CIS.Transactions;
using CIS.Transactions.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Audit;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[DoNotParallelize]
public sealed class AuditSecurityPressureIntegrationTests
{
    [TestMethod]
    public async Task DirectOperationRetainingTheOnlyNativeConnectionCannotHideBridgeDeadlineFailure()
    {
        await using PostgresMessageFixture database = await PostgresMessageFixture.CreateAsync();
        string operationSchema = database.Schema + "_audit_op";
        await database.SqlAsync(PostgreSqlOperationSchema.GetInstallationSql(operationSchema));
        try
        {
            var connectionOptions = new NpgsqlConnectionStringBuilder(database.ConnectionString)
            {
                MaxPoolSize = 1,
                MinPoolSize = 0
            };
            await using NpgsqlDataSource source = NpgsqlDataSource.Create(connectionOptions.ConnectionString);
            var services = new ServiceCollection();
            services.AddLogging();
            var limits = new AuditLimits(4096);
            services.AddSingleton(new MessagingSnapshot(new MessagingOptions { MaximumPayloadBytes = 4096 },
                stores: [new("Messaging", "Messaging")]));
            services.ConfigureCisAudit(new(AuditNativeTestSupport.Route, limits));
            services.AddCisAdoNetDatabase("Messaging", _ => source,
                new()
                {
                    SupportedIsolationLevels = [TransactionIsolation.ReadCommitted]
                });
            services.AddSingleton<IMessagingStore>(database.Store);
            services.AddSingleton(root => new MessageOutbox(root.GetRequiredService<MessagingSnapshot>(), database.Store,
                root.GetRequiredService<MessagingResourceBudget>(), root.GetRequiredService<LocalApplicationIdentity>()));
            services.AddSingleton<IMessageOutbox>(root => root.GetRequiredService<MessageOutbox>());
            services.AddSingleton<IMessagingClient>(root => new MessageSubmissionClient(root.GetRequiredService<MessageOutbox>(),
                root.GetRequiredService<ILocalTransactionFactory>()));
            services.AddSingleton(root => new AuditWriter(new(AuditNativeTestSupport.Route, limits),
                root.GetRequiredService<LocalApplicationIdentity>(), root.GetRequiredService<IMessageOutbox>(),
                root.GetRequiredService<IMessagingClient>()));
            services.AddSingleton<IAuditWriter>(root => root.GetRequiredService<AuditWriter>());
            services.AddCisSecurityAuditBridge(new(submissionTimeout: TimeSpan.FromMilliseconds(250)));
            services.AddCisSecurity(new SecurityOptions());
            // Authorization is the established Operations fixture boundary; the pressure proof concerns
            // an explicit reported-completion hook, not an invented automatic Operations hook.
            var caller = new FixedCaller();
            services.AddSingleton<ICallerContextAccessor>(caller);
            services.AddSingleton<ICapabilityAuthorizer>(new OperationFixtureAuthorizer());
            services.AddSingleton<IOperationJournal>(new PostgreSqlOperationJournal(new()
            {
                Schema = operationSchema
            }));
            services.AddCisOperations();
            services.AddCisOperation(new OperationDefinition<int, string>("AuditPressure", "Messaging", new("fixture.audit.input", 1),
                new NumberNormalizer(), new TextResultCodec(), _ => new FixedScopeResolver(), new()
                {
                    ExecuteCapabilities = ["fixture.execute"],
                    LookupCapabilities = ["fixture.lookup"]
                }));
            await using ServiceProvider application = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
            AuditWriter writer = application.GetRequiredService<AuditWriter>();
            writer.BeginAccepting();
            MessageOutbox outbox = application.GetRequiredService<MessageOutbox>();
            try
            {
                await using AsyncServiceScope scope = application.CreateAsyncScope();
                IDurableOperationService operations = scope.ServiceProvider.GetRequiredService<IDurableOperationService>();
                ILocalTransactionFactory transactions = application.GetRequiredService<ILocalTransactionFactory>();
                await using ILocalTransaction original = await transactions.BeginAsync("Messaging");
                int nativePid = await database.BackendAsync(original.Context);
                Assert.IsTrue(nativePid > 0, "The original transaction has acquired the sole actual provider connection.");
                int handlerCalls = 0;
                await Assert.ThrowsAsync<OperationCanceledException>(() => operations.ExecuteAsync<int, string>(original.Context,
                    new("AuditPressure", "requested-scope", "pressure-key", 7), async (context, _, token) =>
                    {
                        handlerCalls++;
                        Assert.AreSame(original.Context, context);
                        await database.InsertBusinessAsync(context, Guid.NewGuid(), "operation-audit-pressure");
                        await scope.ServiceProvider.GetRequiredService<ISecurityAuditSink>().OperationCompletedAsync(
                            new(caller.Current!, ["fixture.execute"], new(PostgresMessageFixture.Application,
                                PostgresMessageFixture.EnvironmentName, "ledger", "account"),
                                SecurityOperationOutcome.Completed, "reported-before-commit"), token);
                        return OperationDecision<string>.Succeed("unreachable");
                    }));
                Assert.AreEqual(1, handlerCalls);
                Assert.IsTrue(original.Context.IsRollbackOnly);
                Assert.AreEqual(TransactionOutcome.NotCommitted, (await original.CommitAsync()).Outcome);
                await original.DisposeAsync();
                Assert.AreEqual(0L, await database.CountAsync("business_record"));
                Assert.AreEqual(0L, await database.CountAsync("outbox"));
                Assert.AreEqual(0L, await CountJournalAsync(database, operationSchema));

                // The bridge's canceled acquisition actually settled; capacity is available for a new native owner.
                await using ILocalTransaction recovered = await transactions.BeginAsync("Messaging");
                Assert.IsTrue(await database.BackendAsync(recovered.Context) > 0);
                await recovered.RollbackAsync();
            }
            finally
            {
                await writer.DrainAsync();
                await outbox.StopAsync();
            }
        }
        finally
        {
            await database.SqlAsync($"DROP SCHEMA IF EXISTS \"{operationSchema}\" CASCADE");
        }
    }

    [TestMethod]
    [TestCategory("RabbitMq")]
    public async Task NativeMessagingBytePressureReturnsTheActualBridgeRefusalBeforeBusinessWork()
    {
        await using PostgresMessageFixture database = await PostgresMessageFixture.CreateAsync();
        var limits = new AuditLimits(4096);
        LocalApplicationIdentity local = LocalApplicationIdentity.Capture();
        var target = new CapabilityTarget(local.Application, local.Environment, "ledger", "account");
        long minimumBytes = 4L * new MessagingLimits(4096).MaximumEnvelopeBytes;
        var options = new MessagingOptions { MaximumPayloadBytes = 4096, MaximumEncodedBufferBytes = minimumBytes };
        int handlerCalls = 0;
        await using RabbitMqMessageFixture broker = await RabbitMqMessageFixture.CreateAsync(transformSnapshot: initial =>
            new(options, initial.Routes.Append(new MessageRouteDefinition<AuditRecord>(AuditNativeTestSupport.Route,
                MessageKind.Event, new AuditRecordCodec(limits))), [new("Messaging", "Messaging")],
                [new MessageSubscriptionDefinition<int>("A", "Messaging", "events",
                    ((MessageRouteDefinition<int>)initial.GetRoute("events")).Codec,
                    _ => new NumberHandler((_, _, _) =>
                    {
                        Interlocked.Increment(ref handlerCalls);
                        return Task.CompletedTask;
                    }), (_, _, _, _) => ValueTask.FromResult(target), ["fixture.handle"])]));
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        RegisterNativeApplication(builder.Services, broker, database, limits, new(), target, ["fixture.handle"]);
        await using ServiceProvider application = builder.Services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        application.GetRequiredService<AuditWriter>().BeginAccepting();
        var dispatcher = application.GetRequiredService<MessageDispatcher>();
        var transport = application.GetRequiredService<RabbitMqTransport>();
        var refusal = new TaskCompletionSource<AuditSecurityBridgeException>(TaskCreationOptions.RunContinuationsAsynchronously);
        await transport.StartAsync(async (delivery, token) =>
        {
            try
            {
                await dispatcher.DispatchAsync(delivery, token);
                refusal.TrySetException(new InvalidOperationException("The pressure scenario must surface the awaited bridge refusal."));
            }
            catch (AuditSecurityBridgeException error)
            {
                refusal.TrySetResult(error);
            }
            catch (Exception error)
            {
                refusal.TrySetException(error);
            }
            finally
            {
                // Leave this unacknowledged delivery with its real channel owner for joined shutdown.
                delivery.RequestConsumerStop("fixture.audit_pressure_observed");
            }
        });
        try
        {
            await broker.PublishRawAsync(broker.Message().CopyBytes(), "service");
            AuditSecurityBridgeException observed = await refusal.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.AreEqual(AuditSubmissionOutcome.NotSubmitted, observed.Result.Outcome);
            Assert.AreEqual("messaging.buffer_capacity", observed.Result.Error?.Code);
            Assert.IsNull(observed.Result.Receipt);
            Assert.AreEqual(0, handlerCalls);
            Assert.AreEqual(0L, await database.CountAsync("business_record"));
            Assert.AreEqual(0L, await database.CountAsync("outbox"));
            MessagingResourceBudget resources = application.GetRequiredService<MessagingResourceBudget>();
            Assert.IsTrue(resources.TryAcquireBytes(checked((int)minimumBytes), out IDisposable? recovered));
            recovered!.Dispose();
        }
        finally
        {
            await transport.StopAsync();
            await dispatcher.StopAsync();
            await application.GetRequiredService<AuditWriter>().DrainAsync();
            await application.GetRequiredService<MessageOutbox>().StopAsync();
        }
    }

    [TestMethod]
    [TestCategory("RabbitMq")]
    public async Task DedicatedNativeAuditIngressSuppressesOnlyItsRecursiveObservationsAcrossRetryAndLaterDelivery()
    {
        await using PostgresMessageFixture database = await PostgresMessageFixture.CreateAsync();
        var limits = new AuditLimits(4096);
        var codec = new AuditRecordCodec(limits);
        LocalApplicationIdentity local = LocalApplicationIdentity.Capture();
        var target = new CapabilityTarget(local.Application, local.Environment, "audit.ingress", "records-v1");
        int handlerCalls = 0;
        var receivedIds = new List<Guid>();
        await using RabbitMqMessageFixture broker = await RabbitMqMessageFixture.CreateAsync(transformSnapshot: initial =>
            new(initial.Settings, initial.Routes.Append(new MessageRouteDefinition<AuditRecord>(AuditNativeTestSupport.Route,
                MessageKind.Event, codec)), [new("Messaging", "Messaging")],
                [new MessageSubscriptionDefinition<AuditRecord>("A", "Messaging", AuditNativeTestSupport.Route, codec,
                    _ => new AuditHandler(async (context, record, _) =>
                    {
                        // This positive retry uses the local fixture source. It does not prove a foreign forwarding protocol.
                        Assert.AreEqual(context.Envelope.ProducerApplication, record.Producer.Application);
                        Assert.AreEqual(context.Envelope.ProducerEnvironment, record.Producer.Environment);
                        receivedIds.Add(record.AuditId);
                        if (Interlocked.Increment(ref handlerCalls) == 1)
                        {
                            throw new InvalidOperationException("One owned receiver failure exercises actual retained-provenance retry.");
                        }
                        await database.InsertBusinessAsync(context.Transaction, context.Envelope.MessageId, "audit-ingress");
                    }), (_, _, _, _) => ValueTask.FromResult(target), ["audit.receive"],
                    new() { RetryCapabilities = ["audit.retry"] },
                    (_, record, _, original, _, _) => ValueTask.FromResult(record.Producer.Application == original.Application
                        && record.Producer.Environment == original.Environment))], []));
        var bridgeOptions = new AuditSecurityBridgeOptions(ingressBindings:
            [new(target, ["audit.receive"], ["audit.retry"])]);
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        RegisterNativeApplication(builder.Services, broker, database, limits, bridgeOptions, target, ["audit.receive", "audit.retry"]);
        await using ServiceProvider application = builder.Services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        application.GetRequiredService<AuditWriter>().BeginAccepting();
        var transport = application.GetRequiredService<RabbitMqTransport>();
        var dispatcher = application.GetRequiredService<MessageDispatcher>();
        var publisher = application.GetRequiredService<MessageOutboxPublisher>();
        TaskCompletionSource<MessageDispatchResult>[] results = Enumerable.Range(0, 3)
            .Select(_ => new TaskCompletionSource<MessageDispatchResult>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        int deliveries = 0;
        await transport.StartAsync(async (delivery, token) =>
        {
            try
            {
                int index = Interlocked.Increment(ref deliveries) - 1;
                if (index >= results.Length)
                {
                    throw new InvalidOperationException("The no-loop workload permits three native deliveries.");
                }
                results[index].TrySetResult(await dispatcher.DispatchAsync(delivery, token));
            }
            catch (Exception error)
            {
                foreach (TaskCompletionSource<MessageDispatchResult> pending in results)
                {
                    pending.TrySetException(error);
                }
                delivery.RequestConsumerStop("fixture.audit_ingress_failed");
            }
        });
        try
        {
            ISecurityAuditSink sink = application.GetRequiredService<ISecurityAuditSink>();
            var observation = new SecurityAuthorizationAudit(null, ["business.observe"],
                new(local.Application, local.Environment, "ledger", "observed-account"), AuthorizationDecision.Allow);
            await sink.AuthorizationCompletedAsync(observation);
            Assert.AreEqual(1L, await database.CountAsync("outbox"));
            Assert.AreEqual(1, (await publisher.PumpAsync()).Confirmed);
            Assert.AreEqual(MessageDispatchOutcome.Retry, (await results[0].Task.WaitAsync(TimeSpan.FromSeconds(30))).Outcome);
            await RabbitMqMessageFixture.UntilAsync(() => HasEligibleRetryAsync(database));
            Assert.AreEqual(1, (await publisher.PumpAsync()).Confirmed);
            Assert.AreEqual(MessageDispatchOutcome.Handled, (await results[1].Task.WaitAsync(TimeSpan.FromSeconds(30))).Outcome);
            Assert.AreEqual(2L, await database.CountAsync("outbox"), "Only the original Audit publication and its actual retry handoff exist.");

            // A later callback runs after the first bridge invocation and delivery scopes ended.
            await sink.AuthorizationCompletedAsync(observation);
            Assert.AreEqual(3L, await database.CountAsync("outbox"));
            Assert.AreEqual(1, (await publisher.PumpAsync()).Confirmed);
            Assert.AreEqual(MessageDispatchOutcome.Handled, (await results[2].Task.WaitAsync(TimeSpan.FromSeconds(30))).Outcome);
            Assert.AreEqual(3, handlerCalls);
            Assert.AreEqual(receivedIds[0], receivedIds[1]);
            Assert.AreNotEqual(receivedIds[1], receivedIds[2]);
            Assert.AreEqual(3L, await database.CountAsync("outbox"), "Dedicated ingress/retry authorization did not recursively publish new Audit observations.");
            Assert.AreEqual(2L, await database.CountAsync("business_record"));
            Assert.IsTrue(transport.IsReady);
        }
        finally
        {
            await transport.StopAsync();
            await dispatcher.StopAsync();
            await application.GetRequiredService<AuditWriter>().DrainAsync();
            await application.GetRequiredService<MessageOutbox>().StopAsync();
        }
    }

    [TestMethod]
    [TestCategory("RabbitMq")]
    public async Task AuthenticatedNativeEnvelopeCannotGiveAForgedAuditPayloadProducerAcceptance()
    {
        await using PostgresMessageFixture database = await PostgresMessageFixture.CreateAsync();
        var limits = new AuditLimits(4096);
        var codec = new AuditRecordCodec(limits);
        LocalApplicationIdentity local = LocalApplicationIdentity.Capture();
        var target = new CapabilityTarget(local.Application, local.Environment, "audit.ingress", "records-v1");
        int producerChecks = 0;
        int accepted = 0;
        await using RabbitMqMessageFixture broker = await RabbitMqMessageFixture.CreateAsync(transformSnapshot: initial =>
            new(initial.Settings, initial.Routes.Append(new MessageRouteDefinition<AuditRecord>(AuditNativeTestSupport.Route,
                MessageKind.Event, codec)), [new("Messaging", "Messaging")],
                [new MessageSubscriptionDefinition<AuditRecord>("A", "Messaging", AuditNativeTestSupport.Route, codec,
                    _ => new AuditHandler(async (context, record, _) =>
                    {
                        Interlocked.Increment(ref producerChecks);
                        if (record.Producer.Application != context.Envelope.ProducerApplication
                            || record.Producer.Environment != context.Envelope.ProducerEnvironment)
                        {
                            throw new InvalidDataException("fixture.audit_producer_mismatch");
                        }
                        Interlocked.Increment(ref accepted);
                        await database.InsertBusinessAsync(context.Transaction, context.Envelope.MessageId, "accepted-audit-probe");
                    }), (_, _, _, _) => ValueTask.FromResult(target), ["audit.receive"],
                    new() { RetryCapabilities = ["audit.retry"] },
                    (_, record, _, original, _, _) => ValueTask.FromResult(record.Producer.Application == original.Application
                        && record.Producer.Environment == original.Environment))], []));
        var bridgeOptions = new AuditSecurityBridgeOptions(ingressBindings:
            [new(target, ["audit.receive"], ["audit.retry"])]);
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        RegisterNativeApplication(builder.Services, broker, database, limits, bridgeOptions, target, ["audit.receive", "audit.retry"]);
        await using ServiceProvider application = builder.Services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        application.GetRequiredService<AuditWriter>().BeginAccepting();
        var transport = application.GetRequiredService<RabbitMqTransport>();
        var dispatcher = application.GetRequiredService<MessageDispatcher>();
        var observed = new TaskCompletionSource<MessageDispatchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        await transport.StartAsync(async (delivery, token) =>
        {
            try
            {
                observed.TrySetResult(await dispatcher.DispatchAsync(delivery, token));
            }
            catch (Exception error)
            {
                observed.TrySetException(error);
                delivery.RequestConsumerStop("fixture.audit_producer_probe_failed");
            }
        });
        try
        {
            // Adversarial generic receiving data is deliberately sent through a raw test-only seam.
            // It is never promoted into CapturedAuditRecord or passed to the trusted source writer.
            const string forgedPayload = """
                {"auditVersion":1,"auditId":"db1f6c02-57f1-481f-b577-d4e45c68f4e4","eventType":"fixture.audit.changed","eventVersion":1,"category":"business","occurredAtUtc":"2026-11-23T10:00:00Z","recordedAtUtc":"2026-11-23T10:00:00Z","producer":{"application":"forged-other-application","environment":"forged-other-environment"},"actor":{"kind":"unknown"},"outcome":"succeeded","details":{"value":7}}
                """;
            Assert.IsTrue(codec.TryDecode(codec.Contract, Encoding.UTF8.GetBytes(forgedPayload), out AuditRecord? decoded));
            Assert.IsNotNull(decoded);
            EncodedMessage authenticatedEnvelope = MessageWire.Encode(new(Guid.NewGuid(), MessageKind.Event, codec.Contract,
                local.Application, local.Environment, DateTimeOffset.UtcNow, codec.Encode(decoded)), broker.Snapshot.Settings.Limits);
            await broker.PublishRawAsync(authenticatedEnvelope.CopyBytes(), "service",
                exchange: RabbitMqTopology.GetRouteExchange(broker.Options.Capture(), AuditNativeTestSupport.Route));
            MessageDispatchResult refusal = await observed.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.AreEqual(MessageDispatchOutcome.Retry, refusal.Outcome);
            Assert.IsInstanceOfType<InvalidDataException>(refusal.Fault?.SourceException);
            Assert.AreEqual("fixture.audit_producer_mismatch", refusal.Fault?.SourceException.Message);
            Assert.AreEqual(1, producerChecks, "The actual authenticated delivery reached the explicit payload-producer binding check.");
            Assert.AreEqual(0, accepted);
            Assert.AreEqual(0L, await database.CountAsync("business_record"));
            Assert.AreEqual(1L, await database.CountAsync("outbox"));

            await using ILocalTransaction read = await database.Factory.BeginAsync("Messaging");
            MessageProcessingRecord? retained = await database.Store.ReadProcessingAsync(read.Context,
                new(local.Application, local.Environment, "A", authenticatedEnvelope.Envelope.MessageId));
            Assert.IsNotNull(retained);
            Assert.AreEqual(MessageProcessingDisposition.RetryScheduled, retained.Disposition);
            Assert.AreEqual(local.Application, retained.Provenance.Application);
            Assert.AreEqual(local.Environment, retained.Provenance.Environment);
            Assert.AreEqual(authenticatedEnvelope.Digest, retained.Digest);
            Assert.IsTrue(retained.HandoffRecordId.HasValue);
            CollectionAssert.AreEqual(authenticatedEnvelope.CopyBytes(),
                await AuditNativeTestSupport.ReadStoredWireAsync(database, retained.HandoffRecordId.GetValueOrDefault()));
            await read.RollbackAsync();
            // The committed state owns retry responsibility and permits its native ACK; it records no Audit acceptance or archive receipt.
        }
        finally
        {
            await transport.StopAsync();
            await dispatcher.StopAsync();
            await application.GetRequiredService<AuditWriter>().DrainAsync();
            await application.GetRequiredService<MessageOutbox>().StopAsync();
        }
    }

    [TestMethod]
    [TestCategory("RabbitMq")]
    public async Task DedicatedAuditIngressWithoutItsGrantRemainsDeniedWithoutRecursiveAuditPublication()
    {
        await using PostgresMessageFixture database = await PostgresMessageFixture.CreateAsync();
        var limits = new AuditLimits(4096);
        var codec = new AuditRecordCodec(limits);
        LocalApplicationIdentity local = LocalApplicationIdentity.Capture();
        var target = new CapabilityTarget(local.Application, local.Environment, "audit.ingress", "records-v1");
        int handlerCalls = 0;
        await using RabbitMqMessageFixture broker = await RabbitMqMessageFixture.CreateAsync(transformSnapshot: initial =>
            new(initial.Settings, initial.Routes.Append(new MessageRouteDefinition<AuditRecord>(AuditNativeTestSupport.Route,
                MessageKind.Event, codec)), [new("Messaging", "Messaging")],
                [new MessageSubscriptionDefinition<AuditRecord>("A", "Messaging", AuditNativeTestSupport.Route, codec,
                    _ => new AuditHandler(async (context, _, _) =>
                    {
                        Interlocked.Increment(ref handlerCalls);
                        await database.InsertBusinessAsync(context.Transaction, context.Envelope.MessageId, "denied-audit-probe");
                    }), (_, _, _, _) => ValueTask.FromResult(target), ["audit.receive"],
                    new() { RetryCapabilities = ["audit.retry"] },
                    (_, record, _, original, _, _) => ValueTask.FromResult(record.Producer.Application == original.Application
                        && record.Producer.Environment == original.Environment))], []));
        var bridgeOptions = new AuditSecurityBridgeOptions(ingressBindings:
            [new(target, ["audit.receive"], ["audit.retry"])]);
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        RegisterNativeApplication(builder.Services, broker, database, limits, bridgeOptions, target,
            ["audit.receive", "audit.retry"], grantCapabilities: false);
        await using ServiceProvider application = builder.Services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        application.GetRequiredService<AuditWriter>().BeginAccepting();
        var transport = application.GetRequiredService<RabbitMqTransport>();
        var dispatcher = application.GetRequiredService<MessageDispatcher>();
        var observed = new TaskCompletionSource<MessageDispatchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        await transport.StartAsync(async (delivery, token) =>
        {
            try
            {
                observed.TrySetResult(await dispatcher.DispatchAsync(delivery, token));
            }
            catch (Exception error)
            {
                observed.TrySetException(error);
                delivery.RequestConsumerStop("fixture.audit_denial_probe_failed");
            }
        });
        try
        {
            CapturedAuditRecord captured;
            await using (AsyncServiceScope scope = application.CreateAsyncScope())
            {
                captured = scope.ServiceProvider.GetRequiredService<IAuditCapture>()
                    .CaptureLocalSystem(AuditNativeTestSupport.Definition, 23, new(AuditOutcome.Succeeded));
            }
            EncodedMessage incoming = MessageWire.Encode(new(Guid.NewGuid(), MessageKind.Event, codec.Contract,
                local.Application, local.Environment, DateTimeOffset.UtcNow, captured.OriginalBytes), broker.Snapshot.Settings.Limits);
            await broker.PublishRawAsync(incoming.CopyBytes(), "service",
                exchange: RabbitMqTopology.GetRouteExchange(broker.Options.Capture(), AuditNativeTestSupport.Route));
            MessageDispatchResult denial = await observed.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.AreEqual(MessageDispatchOutcome.Terminal, denial.Outcome);
            Assert.AreEqual("messaging.handler_denied", denial.SafeCode);
            Assert.AreEqual(TransactionOutcome.Committed, denial.Completion?.Outcome);
            Assert.AreEqual(0, handlerCalls, "A dedicated observation omission does not grant authority to its authenticated publisher.");
            Assert.AreEqual(0L, await database.CountAsync("business_record"));
            Assert.AreEqual(0L, await database.CountAsync("processing"));
            Assert.AreEqual(1L, await database.CountAsync("rejections"));
            Assert.AreEqual(1L, await database.CountAsync("outbox"));
            await AssertRetainedDenialAsync(database, broker, local, incoming);
            Assert.IsTrue(transport.IsReady);
            // The committed state secures native rejection/quarantine responsibility, not Audit acceptance or archive storage.
        }
        finally
        {
            await transport.StopAsync();
            await dispatcher.StopAsync();
            await application.GetRequiredService<AuditWriter>().DrainAsync();
            await application.GetRequiredService<MessageOutbox>().StopAsync();
        }
    }

    private static async Task AssertRetainedDenialAsync(PostgresMessageFixture database, RabbitMqMessageFixture broker,
        LocalApplicationIdentity local, EncodedMessage incoming)
    {
        // The fixture's reservation convenience method uses its own fixed source namespace.
        // Inspect this application's actual committed rejection and mandatory outgoing FK directly.
        await using NpgsqlConnection connection = await database.Source.OpenConnectionAsync();
        await using var command = new NpgsqlCommand($"""
            SELECT rejected.application,rejected.environment,rejected.subscription,rejected.message_id,
                rejected.digest,rejected.provenance,rejected.safe_code,
                outgoing.purpose,outgoing.target_subscription,outgoing.digest,outgoing.envelope,outgoing.provenance,
                rejected.created_xid=outgoing.staged_xid,
                (SELECT count(*) FROM {database.Table("outbox")} WHERE purpose=0),
                outgoing.application,outgoing.environment,outgoing.message_id
            FROM {database.Table("rejections")} rejected
            JOIN {database.Table("outbox")} outgoing ON outgoing.record_id=rejected.handoff_id
            WHERE rejected.application=@application AND rejected.environment=@environment
                AND rejected.subscription='A' AND rejected.message_id=@message
            """, connection);
        command.Parameters.AddWithValue("application", local.Application);
        command.Parameters.AddWithValue("environment", local.Environment);
        command.Parameters.AddWithValue("message", incoming.Envelope.MessageId);
        await using NpgsqlDataReader row = await command.ExecuteReaderAsync();
        Assert.IsTrue(await row.ReadAsync());
        Assert.AreEqual(local.Application, row.GetString(0));
        Assert.AreEqual(local.Environment, row.GetString(1));
        Assert.AreEqual("A", row.GetString(2));
        Assert.AreEqual(incoming.Envelope.MessageId, row.GetGuid(3));
        CollectionAssert.AreEqual(incoming.Digest.CopyBytes(), row.GetFieldValue<byte[]>(4));
        MessageProvenance? original = JsonSerializer.Deserialize<MessageProvenance>(row.GetString(5));
        Assert.IsNotNull(original);
        Assert.AreEqual(local.Application, original.Application);
        Assert.AreEqual(local.Environment, original.Environment);
        Assert.AreEqual(broker.ServiceUser, original.UserId);
        Assert.AreEqual(new SecuritySubject(broker.Options.Capture().SubjectIssuer, broker.ServiceUser), original.Subject);
        Assert.AreEqual("messaging.handler_denied", row.GetString(6));
        Assert.AreEqual((short)MessageRecordPurpose.Rejection, row.GetInt16(7));
        Assert.AreEqual("A", row.GetString(8));
        CollectionAssert.AreEqual(incoming.Digest.CopyBytes(), row.GetFieldValue<byte[]>(9));
        CollectionAssert.AreEqual(incoming.CopyBytes(), row.GetFieldValue<byte[]>(10));
        Assert.AreEqual(original, JsonSerializer.Deserialize<MessageProvenance>(row.GetString(11)));
        Assert.IsTrue(row.GetBoolean(12), "Rejection metadata and its exact outgoing responsibility committed in one native transaction.");
        Assert.AreEqual(0L, row.GetInt64(13), "The selected bridge produced no recursive application Audit publication.");
        Assert.AreEqual(local.Application, row.GetString(14));
        Assert.AreEqual(local.Environment, row.GetString(15));
        Assert.AreEqual(incoming.Envelope.MessageId, row.GetGuid(16));
        Assert.IsFalse(await row.ReadAsync());
    }

    internal static void RegisterNativeApplication(IServiceCollection services, RabbitMqMessageFixture broker,
        PostgresMessageFixture database, AuditLimits limits, AuditSecurityBridgeOptions? bridgeOptions = null,
        CapabilityTarget? grantTarget = null, IReadOnlyList<string>? capabilities = null, bool grantCapabilities = true)
    {
        // The fixture owns infrastructure; this collection is the one application provider under test.
        // ConfigureCisAudit supplies the route before the existing Messaging graph freezes.
        services.AddSingleton(new MessagingSnapshot(broker.Snapshot.Settings,
            broker.Snapshot.Routes.Where(route => route.Alias != AuditNativeTestSupport.Route), broker.Snapshot.Stores,
            broker.Snapshot.Subscriptions, broker.Snapshot.Requests));
        services.ConfigureCisAudit(new(AuditNativeTestSupport.Route, limits));
        services.AddCisAdoNetDatabase("Messaging", _ => database.Boundary,
            new()
            {
                SupportedIsolationLevels = [TransactionIsolation.ReadCommitted]
            });
        services.AddSingleton<IMessagingStore>(database.Store);
        if (bridgeOptions is not null)
        {
            services.AddCisSecurityAuditBridge(bridgeOptions);
        }
        RabbitMqSettings settings = broker.Options.Capture();
        var security = new SecurityOptions { PolicyVersion = "rm06-native" };
        SecuritySubject subject = new(settings.SubjectIssuer, broker.ServiceUser);
        security.Profiles.Add(new(settings.SecurityProfile, SecurityProfileKind.Native, SecurityProfileRoles.Inbound,
            "cis-rabbitmq", [new(subject, broker.Local.Application, broker.Local.Environment)]));
        CapabilityTarget selectedTarget = grantTarget ?? new(broker.Local.Application, broker.Local.Environment);
        foreach (string capability in capabilities ?? ["fixture.handle"])
        {
            security.Capabilities.Add(capability);
            if (grantCapabilities)
            {
                security.Grants.Add(new(settings.SecurityProfile, capability, selectedTarget.Application, selectedTarget.Environment,
                    subject: subject,
                    resourceSelection: selectedTarget.ResourceType is null ? CapabilityResourceSelection.TargetOnly : CapabilityResourceSelection.ExactResource,
                    resourceType: selectedTarget.ResourceType, resourceIdentifier: selectedTarget.ResourceIdentifier));
            }
        }
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, BrokerOnlyScheme>("cis-rabbitmq", _ => { });
        services.AddCisSecurity(security);
        services.AddCisRabbitMqMessaging(broker.Options);
        services.AddCisMessaging();
        services.AddCisAudit();
    }

    private static async Task<bool> HasEligibleRetryAsync(PostgresMessageFixture database)
    {
        await using NpgsqlConnection connection = await database.Source.OpenConnectionAsync();
        await using var command = new NpgsqlCommand($"SELECT EXISTS (SELECT 1 FROM {database.Table("outbox")} WHERE purpose=1 AND state=0 AND eligible_at<=clock_timestamp())", connection);
        return await command.ExecuteScalarAsync() is true;
    }

    private static async Task<long> CountJournalAsync(PostgresMessageFixture database, string schema)
    {
        await using NpgsqlConnection connection = await database.Source.OpenConnectionAsync();
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM \"{schema}\".operation_journal", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private sealed class FixedCaller : ICallerContextAccessor
    {
        public CallerContext? Current
        {
            get;
        } = new("fixture", "validated", new("fixture-issuer", "application"),
            SecurityActorKind.Application, "initiator", "test", new(DateTimeOffset.UtcNow), "fixture-policy");
    }

    private sealed class NumberNormalizer : IOperationRequestNormalizer<int>
    {
        public NormalizedOperationInput<int> Normalize(int input, OperationScope scope)
            => new(input, [new(new("fixture.audit.input", 1), OperationCanonicalValue.Integer(input))]);
    }

    private sealed class TextResultCodec : IOperationResultCodec<string>
    {
        public OperationContract CurrentContract { get; } = new("fixture.audit.result", 1);

        public IReadOnlyList<OperationContract> ReadableContracts { get; } = [new("fixture.audit.result", 1)];

        public byte[] Encode(string? value) => System.Text.Encoding.UTF8.GetBytes(value ?? throw new ArgumentNullException(nameof(value)));

        public bool TryDecode(OperationContract contract, ReadOnlyMemory<byte> value, out string? decoded)
        {
            decoded = contract == CurrentContract ? System.Text.Encoding.UTF8.GetString(value.Span) : null;
            return decoded is not null;
        }
    }

    private sealed class FixedScopeResolver : IOperationScopeResolver
    {
        public ValueTask<OperationScope> ResolveAsync(string operationName, string requestedScope, CallerContext? caller,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(new OperationScope("trusted-scope", "ledger", "account"));
    }

    private sealed class NumberHandler(Func<MessageHandlerContext, int, CancellationToken, Task> handler) : IMessageHandler<int>
    {
        public Task HandleAsync(MessageHandlerContext context, int value, CancellationToken cancellationToken = default)
            => handler(context, value, cancellationToken);
    }

    private sealed class AuditHandler(Func<MessageHandlerContext, AuditRecord, CancellationToken, Task> handler) : IMessageHandler<AuditRecord>
    {
        public Task HandleAsync(MessageHandlerContext context, AuditRecord record, CancellationToken cancellationToken = default)
            => handler(context, record, cancellationToken);
    }

    private sealed class BrokerOnlyScheme(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }
}

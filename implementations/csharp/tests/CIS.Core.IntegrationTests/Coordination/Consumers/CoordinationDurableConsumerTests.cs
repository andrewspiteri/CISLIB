using System.Data.Common;
using CIS.Coordination;
using CIS.Coordination.PostgreSql;
using CIS.Messaging;
using CIS.Operations;
using CIS.Operations.Hosting;
using CIS.Operations.PostgreSql;
using CIS.Security;
using CIS.Tests.IntegrationTests.Messaging;
using CIS.Tests.IntegrationTests.Operations;
using CIS.Tests.MessagingCrashFixture;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Coordination.Consumers;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("RabbitMq")]
[DoNotParallelize]
public sealed class CoordinationDurableConsumerTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task GuardJournalBusinessAndOutgoingShareTheOriginalCommitAndRemainRecoverableAfterOwnershipEnds(bool loseCommitAcknowledgement)
    {
        await using PostgresMessageFixture database = await PostgresMessageFixture.CreateAsync();
        await using RabbitMqMessageFixture broker = await RabbitMqMessageFixture.CreateAsync();
        string operationSchema = database.Schema + "_op";
        string coordinationSchema = database.Schema + "_coord";
        Guid authorityId = Guid.NewGuid();
        bool operationOwned = false;
        bool coordinationOwned = false;
        var snapshot = new MessagingSnapshot(new MessagingOptions(), broker.Snapshot.Routes, [new("Messaging", "Messaging")]);
        await using var resources = new MessagingResourceBudget(snapshot.Settings);
        var outbox = new MessageOutbox(snapshot, database.Store, resources, broker.Local);
        await using var publisher = new MessageOutboxPublisher(snapshot, database.Store, database.Factory,
            broker.Transport, resources, broker.Local);
        var received = new TaskCompletionSource<EncodedMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        await broker.Transport.StartAsync(async (delivery, token) =>
        {
            try
            {
                Assert.IsNotNull(await delivery.GetNativeEvidenceAsync(token));
                Assert.IsTrue(MessageWire.TryDecode(delivery.Body, out EncodedMessage? message, out _));
                await delivery.AcknowledgeAsync(token);
                received.TrySetResult(message!);
            }
            catch (Exception error)
            {
                received.TrySetException(error);
                delivery.RequestConsumerStop("fixture.coordination_capture_failed");
            }
        });

        try
        {
            operationOwned = true;
            await database.SqlAsync(PostgreSqlOperationSchema.GetInstallationSql(operationSchema));
            coordinationOwned = true;
            await database.SqlAsync(PostgreSqlCoordinationSchema.GetInstallationSql(authorityId, coordinationSchema));
            var authority = new PostgreSqlCoordinationProvider(database.Factory, "Messaging", new()
            {
                AuthorityId = authorityId,
                Schema = coordinationSchema
            });
            LocalApplicationIdentity local = broker.Local;
            var key = new CoordinationKey(local.Environment, "wallet-service", "wallet.compaction", "partition-12");
            CoordinationAcquireResult first = await authority.AcquireAsync(new(
                new(key, local.Application, local.InstanceId, Guid.NewGuid()), TimeSpan.FromSeconds(30)));
            Assert.AreEqual(CoordinationAcquireOutcome.Acquired, first.Outcome);
            CoordinationGrant originalGrant = first.Grant!;
            var services = new ServiceCollection();
            services.AddSingleton<ILocalTransactionFactory>(database.Factory);
            services.AddSingleton<IOperationJournal>(new PostgreSqlOperationJournal(new()
            {
                Schema = operationSchema
            }));
            services.AddScoped<CoordinationConsumerAuthorization>();
            services.AddScoped<ICapabilityAuthorizer>(root => root.GetRequiredService<CoordinationConsumerAuthorization>());
            services.AddScoped<ICallerContextAccessor>(_ => new OperationFixtureCaller());
            services.AddCisOperations();
            services.AddCisOperation(CrashContracts.Operation());
            await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            IDurableOperationService operations = scope.ServiceProvider.GetRequiredService<IDurableOperationService>();
            var request = new OperationRequest<int>("Submit", "requested-account", "same-durable-operation", 7);
            Guid businessId = Guid.NewGuid();
            MessageStageReceipt? receipt = null;
            byte[]? originalWire = null;
            int handlerCalls = 0;
            await using ILocalTransaction original = await database.Factory.BeginAsync("Messaging");
            OperationExecution<string> prepared = await operations.ExecuteAsync<int, string>(original.Context, request,
                async (context, value, token) =>
                {
                    Assert.AreSame(original.Context, context);
                    handlerCalls++;
                    (await authority.ValidateAsync(context, originalGrant, token)).ThrowIfRejected();
                    await database.InsertBusinessAsync(context, businessId, "coordination");
                    MessageStageResult staged = await outbox.PublishAsync(context, "events", value, cancellationToken: token);
                    Assert.AreEqual(MessageStageDisposition.Staged, staged.Disposition);
                    receipt = staged.Receipt!;
                    originalWire = await ReadWireAsync(context, database, receipt.RecordId);
                    return OperationDecision<string>.Succeed("guarded:" + value);
                });
            Assert.AreEqual(OperationExecutionDisposition.Prepared, prepared.Disposition);
            Assert.IsNotNull(receipt);
            Assert.IsNull(original.Completion);
            Assert.AreEqual(0L, await database.CountAsync("business_record"));
            Assert.AreEqual(0L, await database.CountAsync("outbox"));
            Assert.AreEqual(0L, await CountJournalAsync(database.Source, operationSchema));
            Assert.AreEqual(OperationLookupDisposition.NotFound,
                (await operations.GetResultAsync<string>(new("Submit", "requested-account", request.Key))).Disposition);
            Assert.AreEqual(0, (await publisher.PumpAsync()).Attempted);
            Assert.IsFalse(received.Task.IsCompleted);
            int commitsBefore = database.Boundary.ConfirmedNativeCommits;
            if (loseCommitAcknowledgement)
            {
                database.Boundary.FailAfterNextCommit();
            }

            TransactionCompletion completion = await original.CommitAsync();
            Assert.AreEqual(loseCommitAcknowledgement ? TransactionOutcome.Unknown : TransactionOutcome.Committed, completion.Outcome);
            Assert.AreEqual(commitsBefore + 1, database.Boundary.ConfirmedNativeCommits,
                "The Unknown seam reports a lost acknowledgement only after an actual successful provider commit.");
            Assert.AreEqual(1L, await database.CountAsync("business_record"));
            Assert.AreEqual(1L, await database.CountAsync("outbox"));
            Assert.AreEqual(1L, await CountJournalAsync(database.Source, operationSchema));
            await AssertSameNativeTransactionAsync(database, operationSchema);
            OperationLookup<string> found = await operations.GetResultAsync<string>(new("Submit", "requested-account", request.Key));
            Assert.AreEqual(OperationLookupDisposition.Found, found.Disposition);
            Assert.AreEqual("guarded:7", found.Decision!.Value);
            Assert.IsTrue(scope.ServiceProvider.GetRequiredService<CoordinationConsumerAuthorization>().Checks >= 3);
            Assert.AreEqual(CoordinationMutationOutcome.Succeeded, (await authority.ReleaseAsync(originalGrant)).Outcome);
            CoordinationAcquireResult successor = await authority.AcquireAsync(new(
                new(key, local.Application, local.InstanceId, Guid.NewGuid()), TimeSpan.FromSeconds(30)));
            Assert.AreEqual(CoordinationAcquireOutcome.Acquired, successor.Outcome);
            Assert.IsTrue(successor.Grant!.Generation > originalGrant.Generation);

            await using (ILocalTransaction stale = await database.Factory.BeginAsync("Messaging"))
            {
                Assert.IsFalse((await authority.ValidateAsync(stale.Context, originalGrant)).Admitted);
                Assert.AreEqual(TransactionOutcome.NotCommitted, (await stale.CommitAsync()).Outcome);
            }

            await using (ILocalTransaction retry = await database.Factory.BeginAsync("Messaging"))
            {
                OperationExecution<string> replayed = await operations.ExecuteAsync<int, string>(retry.Context, request,
                    (_, _, _) => throw new AssertFailedException("Takeover must replay the original durable operation without another handler."));
                Assert.AreEqual(OperationExecutionDisposition.Replayed, replayed.Disposition);
                Assert.AreEqual(found.Decision.Value, replayed.Decision!.Value);
                Assert.AreEqual(TransactionOutcome.Committed, (await retry.CommitAsync()).Outcome);
            }

            Assert.AreEqual(1, handlerCalls);
            Assert.AreEqual(CoordinationMutationOutcome.Succeeded, (await authority.ReleaseAsync(successor.Grant)).Outcome);
            MessagePumpResult publication = await publisher.PumpAsync();
            Assert.AreEqual(1, publication.Confirmed);
            Assert.AreEqual(TransactionOutcome.Committed, publication.ReservationCompletion!.Outcome);
            Assert.AreEqual(TransactionOutcome.Committed, publication.Attempts.Single().SettlementCompletion!.Outcome);
            EncodedMessage delivered = await received.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.AreEqual(receipt.MessageId, delivered.Envelope.MessageId);
            Assert.AreEqual(receipt.Digest, delivered.Digest);
            CollectionAssert.AreEqual(originalWire!, delivered.CopyBytes());
            await using ILocalTransaction inspect = await database.Factory.BeginAsync("Messaging");
            MessageInspectionRecord retained = (await database.Store.InspectAsync(inspect.Context,
                new(local.Application, local.Environment, RouteAlias: "events"))).Single();
            Assert.AreEqual("outbox.published", retained.State);
            Assert.AreEqual(receipt.RecordId, retained.RecordId);
            await inspect.RollbackAsync();
        }
        finally
        {
            await outbox.StopAsync();
            if (coordinationOwned)
            {
                await database.SqlAsync($"DROP SCHEMA IF EXISTS \"{coordinationSchema}\" CASCADE");
            }
            if (operationOwned)
            {
                await database.SqlAsync($"DROP SCHEMA IF EXISTS \"{operationSchema}\" CASCADE");
            }
        }
    }

    private static async Task<byte[]> ReadWireAsync(ILocalTransactionContext context, PostgresMessageFixture database, Guid record)
        => await context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            await using DbCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"SELECT envelope FROM {database.Table("outbox")} WHERE record_id=@record";
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = "record";
            parameter.Value = record;
            command.Parameters.Add(parameter);
            return (byte[])(await command.ExecuteScalarAsync(token))!;
        });

    private static async Task<long> CountJournalAsync(NpgsqlDataSource source, string schema)
    {
        await using NpgsqlConnection connection = await source.OpenConnectionAsync();
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM \"{schema}\".operation_journal", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task AssertSameNativeTransactionAsync(PostgresMessageFixture database, string operationSchema)
    {
        await using NpgsqlConnection connection = await database.Source.OpenConnectionAsync();
        await using var command = new NpgsqlCommand($"""
            SELECT journal.claim_xid = business.native_xid AND business.native_xid = outgoing.staged_xid
            FROM "{operationSchema}".operation_journal journal
            CROSS JOIN {database.Table("business_record")} business
            CROSS JOIN {database.Table("outbox")} outgoing
            """, connection);
        Assert.AreEqual(true, await command.ExecuteScalarAsync(), "Journal, protected effect and outgoing intent use one original native transaction.");
    }
}

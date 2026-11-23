using System.Data.Common;
using System.Text.Json;
using CIS.Audit;
using CIS.Messaging;
using CIS.Security;
using CIS.Tests.IntegrationTests.Messaging;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Audit;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[DoNotParallelize]
public sealed class AuditTransactionIntegrationTests
{
    [TestMethod]
    [DataRow("commit")]
    [DataRow("rollback")]
    [DataRow("unfinished")]
    public async Task OriginalOwnerControlsBusinessAndAuditVisibilityTogether(string completion)
    {
        await using PostgresMessageFixture database = await PostgresMessageFixture.CreateAsync();
        LocalApplicationIdentity local = LocalApplicationIdentity.Capture();
        var limits = new AuditLimits(4096);
        MessagingSnapshot snapshot = AuditNativeTestSupport.Snapshot(limits);
        await using var resources = new MessagingResourceBudget(snapshot.Settings);
        await using var capture = new AuditCaptureEngine(local, limits, TimeProvider.System, resources);
        var outbox = new MessageOutbox(snapshot, database.Store, resources, local);
        await using var writer = new AuditWriter(new(AuditNativeTestSupport.Route, limits), local, outbox,
            new MessageSubmissionClient(outbox, database.Factory));
        writer.BeginAccepting();
        try
        {
            CapturedAuditRecord record = AuditNativeTestSupport.Capture(capture);
            await using ILocalTransaction owner = await database.Factory.BeginAsync("Messaging");
            Guid businessId = Guid.NewGuid();
            await database.InsertBusinessAsync(owner.Context, businessId, "audit-source");
            AuditStageResult staged = await writer.StageAsync(owner.Context, record);
            Assert.AreEqual(AuditStageDisposition.Staged, staged.Disposition);
            Assert.AreEqual(record.AuditId, staged.AuditId);
            Assert.IsNotNull(staged.Receipt);
            Assert.IsNull(owner.Completion, "Staging leaves completion with the application's original owner.");
            byte[] stored = await AuditNativeTestSupport.ReadWireAsync(owner.Context, database, staged.Receipt.RecordId);
            Assert.IsTrue(MessageWire.TryDecode(stored, out EncodedMessage? envelope, out _));
            CollectionAssert.AreEqual(record.OriginalBytes.ToArray(), envelope!.Envelope.CopyPayload());
            Assert.AreEqual(staged.Receipt.MessageId, envelope.Envelope.MessageId);
            Assert.AreEqual(0L, await database.CountAsync("business_record"));
            Assert.AreEqual(0L, await database.CountAsync("outbox"));

            if (completion == "commit")
            {
                Assert.AreEqual(TransactionOutcome.Committed, (await owner.CommitAsync()).Outcome);
            }
            else if (completion == "rollback")
            {
                Assert.AreEqual(TransactionOutcome.NotCommitted, (await owner.RollbackAsync()).Outcome);
            }
            else
            {
                await owner.DisposeAsync();
                Assert.AreEqual(TransactionOutcome.NotCommitted, owner.Completion?.Outcome);
            }

            long expected = completion == "commit" ? 1 : 0;
            Assert.AreEqual(expected, await database.CountAsync("business_record"));
            Assert.AreEqual(expected, await database.CountAsync("outbox"));
            if (expected == 1)
            {
                CollectionAssert.AreEqual(stored, await AuditNativeTestSupport.ReadStoredWireAsync(database, staged.Receipt.RecordId));
            }
        }
        finally
        {
            await outbox.StopAsync();
        }
    }

    [TestMethod]
    public async Task IgnoredAuditValidationRefusalPoisonsTheOriginalNativeTransaction()
    {
        await using PostgresMessageFixture database = await PostgresMessageFixture.CreateAsync();
        LocalApplicationIdentity local = LocalApplicationIdentity.Capture();
        var captureLimits = new AuditLimits(4096);
        MessagingSnapshot snapshot = AuditNativeTestSupport.Snapshot(captureLimits);
        await using var resources = new MessagingResourceBudget(snapshot.Settings);
        await using var capture = new AuditCaptureEngine(local, captureLimits, TimeProvider.System, resources);
        var outbox = new MessageOutbox(snapshot, database.Store, resources, local);
        // A retained valid capture can meet a later writer with a smaller selected complete-body profile.
        await using var writer = new AuditWriter(new(AuditNativeTestSupport.Route, new(256)), local, outbox,
            new MessageSubmissionClient(outbox, database.Factory));
        writer.BeginAccepting();
        try
        {
            CapturedAuditRecord record = AuditNativeTestSupport.Capture(capture);
            Assert.IsTrue(record.OriginalBytes.Length > 256);
            await using ILocalTransaction owner = await database.Factory.BeginAsync("Messaging");
            await database.InsertBusinessAsync(owner.Context, Guid.NewGuid(), "ignored-audit-refusal");
            AuditStageResult refused = await writer.StageAsync(owner.Context, record);
            Assert.AreEqual(AuditStageDisposition.Unavailable, refused.Disposition);
            Assert.AreEqual("audit.record_too_large", refused.Error?.Code);
            Assert.IsTrue(owner.Context.IsRollbackOnly);

            // Deliberately ignore the refusal and attempt the application's apparently successful commit.
            Assert.AreEqual(TransactionOutcome.NotCommitted, (await owner.CommitAsync()).Outcome);
            Assert.AreEqual(0L, await database.CountAsync("business_record"));
            Assert.AreEqual(0L, await database.CountAsync("outbox"));
        }
        finally
        {
            await outbox.StopAsync();
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StandaloneSubmissionPreservesKnownAndUncertainActualNativeCommit(bool loseCommitAcknowledgement)
    {
        await using PostgresMessageFixture database = await PostgresMessageFixture.CreateAsync();
        LocalApplicationIdentity local = LocalApplicationIdentity.Capture();
        var limits = new AuditLimits(4096);
        MessagingSnapshot snapshot = AuditNativeTestSupport.Snapshot(limits);
        await using var resources = new MessagingResourceBudget(snapshot.Settings);
        await using var capture = new AuditCaptureEngine(local, limits, TimeProvider.System, resources);
        var outbox = new MessageOutbox(snapshot, database.Store, resources, local);
        await using var writer = new AuditWriter(new(AuditNativeTestSupport.Route, limits), local, outbox,
            new MessageSubmissionClient(outbox, database.Factory));
        writer.BeginAccepting();
        try
        {
            CapturedAuditRecord record = AuditNativeTestSupport.Capture(capture);
            int nativeCommits = database.Boundary.ConfirmedNativeCommits;
            if (loseCommitAcknowledgement)
            {
                database.Boundary.FailAfterNextCommit();
            }

            AuditSubmissionResult result = await writer.SubmitAsync(record);
            Assert.AreEqual(loseCommitAcknowledgement ? AuditSubmissionOutcome.Unknown : AuditSubmissionOutcome.OutboxCommitted, result.Outcome);
            Assert.AreEqual(loseCommitAcknowledgement ? TransactionOutcome.Unknown : TransactionOutcome.Committed, result.Completion?.Outcome);
            Assert.AreEqual(record.AuditId, result.AuditId);
            Assert.IsNotNull(result.Receipt);
            Assert.AreEqual(nativeCommits + 1, database.Boundary.ConfirmedNativeCommits);
            Assert.AreEqual(1L, await database.CountAsync("outbox"), "The injected failure follows one real native commit.");
            byte[] stored = await AuditNativeTestSupport.ReadStoredWireAsync(database, result.Receipt.RecordId);
            Assert.IsTrue(MessageWire.TryDecode(stored, out EncodedMessage? envelope, out _));
            CollectionAssert.AreEqual(record.OriginalBytes.ToArray(), envelope!.Envelope.CopyPayload());
            Assert.AreEqual(result.Receipt.MessageId, envelope.Envelope.MessageId);
            Assert.AreEqual(result.Receipt.Digest, envelope.Digest);
            Assert.AreEqual(0L, await database.CountAsync("business_record"));
        }
        finally
        {
            await outbox.StopAsync();
        }
    }

    [TestMethod]
    public async Task ActualDurableStorageRefusalRemainsKnownNotSubmitted()
    {
        await using PostgresMessageFixture database = await PostgresMessageFixture.CreateAsync(capacity: 1);
        LocalApplicationIdentity local = LocalApplicationIdentity.Capture();
        var limits = new AuditLimits(4096);
        MessagingSnapshot snapshot = AuditNativeTestSupport.Snapshot(limits);
        await using var resources = new MessagingResourceBudget(snapshot.Settings);
        await using var capture = new AuditCaptureEngine(local, limits, TimeProvider.System, resources);
        var outbox = new MessageOutbox(snapshot, database.Store, resources, local);
        await using var writer = new AuditWriter(new(AuditNativeTestSupport.Route, limits), local, outbox,
            new MessageSubmissionClient(outbox, database.Factory));
        writer.BeginAccepting();
        try
        {
            AuditSubmissionResult refused = await writer.SubmitAsync(AuditNativeTestSupport.Capture(capture));
            Assert.AreEqual(AuditSubmissionOutcome.NotSubmitted, refused.Outcome);
            Assert.AreEqual(TransactionOutcome.NotCommitted, refused.Completion?.Outcome);
            Assert.AreEqual("messaging.storage_capacity", refused.Error?.Code);
            Assert.IsNull(refused.Receipt);
            Assert.AreEqual(0L, await database.CountAsync("outbox"));
        }
        finally
        {
            await outbox.StopAsync();
        }
    }
}

internal static class AuditNativeTestSupport
{
    internal const string Route = "audit";

    internal static AuditEventDefinition<int> Definition
    {
        get;
    } = new("fixture.audit.changed", 1,
        AuditCategory.Business, new NumberDetailsCodec());

    internal static MessagingSnapshot Snapshot(AuditLimits limits, MessagingOptions? options = null)
        => new(options ?? new(), [new MessageRouteDefinition<AuditRecord>(Route, MessageKind.Event, new AuditRecordCodec(limits))],
            [new("Messaging", "Messaging")]);

    internal static CapturedAuditRecord Capture(AuditCaptureEngine engine, int value = 7)
    {
        IAuditCapture capture = new AuditCapture(engine, null);
        return capture.CaptureLocalSystem(Definition, value, new(AuditOutcome.Succeeded));
    }

    internal static Task<byte[]> ReadWireAsync(ILocalTransactionContext context, PostgresMessageFixture database, Guid recordId)
        => context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            await using DbCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"SELECT envelope FROM {database.Table("outbox")} WHERE record_id=@id";
            DbParameter id = command.CreateParameter();
            id.ParameterName = "id";
            id.Value = recordId;
            command.Parameters.Add(id);
            return (byte[])(await command.ExecuteScalarAsync(token))!;
        });

    internal static async Task<byte[]> ReadStoredWireAsync(PostgresMessageFixture database, Guid recordId)
    {
        await using NpgsqlConnection connection = await database.Source.OpenConnectionAsync();
        await using var command = new NpgsqlCommand($"SELECT envelope FROM {database.Table("outbox")} WHERE record_id=@id", connection);
        command.Parameters.AddWithValue("id", recordId);
        return (byte[])(await command.ExecuteScalarAsync())!;
    }

    private sealed class NumberDetailsCodec : IAuditDetailsCodec<int>
    {
        public void Validate(int details)
        {
        }

        public void Write(Utf8JsonWriter writer, int details)
        {
            writer.WriteStartObject();
            writer.WriteNumber("value", details);
            writer.WriteEndObject();
        }
    }
}

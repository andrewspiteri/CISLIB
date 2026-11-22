using System.Data.Common;
using System.Text.Json;
using CIS.Messaging;
using CIS.Security;
using CIS.Transactions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Messaging;

[TestClass]
[DoNotParallelize]
public sealed class PostgresMessageReplyTests
{
    [TestMethod]
    public async Task FreshTerminalReplyAttachmentRequiresExactVersionOrdinalGenerationAndTheSameNativeOwner()
    {
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync();
        MessageProcessingAttempt attempt = Request();
        Guid replyId;
        await using (ILocalTransaction owner = await fixture.Factory.BeginAsync("Messaging"))
        {
            MessageDispositionResult terminal = await TerminalAsync(fixture, owner.Context, attempt);
            MessageOutgoingRecord reply = Reply(attempt);
            replyId = reply.RecordId;
            await fixture.Store.StageAsync(owner.Context, reply);
            var expected = new MessageReplyAttachment(replyId, attempt.Key, terminal.Record!.Version, 1);
            Assert.IsFalse(await fixture.Store.AttachReplyAsync(owner.Context, expected with { ExpectedVersion = expected.ExpectedVersion + 1 }));
            Assert.IsFalse(await fixture.Store.AttachReplyAsync(owner.Context, expected with { ExpectedOrdinal = 2 }));
            Assert.IsFalse(await fixture.Store.AttachReplyAsync(owner.Context, expected with { Generation = 1 }));
            Assert.IsTrue(await fixture.Store.AttachReplyAsync(owner.Context, expected));
            Assert.IsFalse(await fixture.Store.AttachReplyAsync(owner.Context, expected));
            Assert.AreEqual(TransactionOutcome.Committed, (await owner.CommitAsync()).Outcome);
        }
        await using ILocalTransaction duplicate = await fixture.Factory.BeginAsync("Messaging");
        MessageProcessingClaim claim = await fixture.Store.ClaimProcessingAsync(duplicate.Context, attempt);
        Assert.AreEqual(MessageClaimDisposition.Terminal, claim.Disposition);
        Assert.AreEqual(replyId, claim.Record!.ReplyRecordId);
        Assert.IsFalse(await fixture.Store.AttachReplyAsync(duplicate.Context,
            new(replyId, attempt.Key, claim.Record.Version, 1)));
        await duplicate.RollbackAsync();
        Assert.AreEqual(2L, await fixture.CountAsync("outbox"));
    }

    [TestMethod]
    public async Task ANewOwnerCannotStageAReplyAgainstPreviouslyCommittedTerminalOrRejectionAnchors()
    {
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync();
        MessageProcessingAttempt attempt = Request();
        Guid rejectionId = Guid.NewGuid();
        await using (ILocalTransaction owner = await fixture.Factory.BeginAsync("Messaging"))
        {
            await TerminalAsync(fixture, owner.Context, attempt);
            await fixture.Store.RecordRejectionAsync(owner.Context, Rejection(attempt, rejectionId));
            await owner.CommitAsync();
        }
        foreach (bool rejected in new[] { false, true })
        {
            await using ILocalTransaction foreign = await fixture.Factory.BeginAsync("Messaging");
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Store.StageAsync(foreign.Context,
                Reply(attempt, rejected ? rejectionId : null)));
            Assert.IsTrue(foreign.Context.IsRollbackOnly);
            Assert.AreEqual(TransactionOutcome.NotCommitted, (await foreign.CommitAsync()).Outcome);
        }
        Assert.AreEqual(2L, await fixture.CountAsync("outbox"));
    }

    [TestMethod]
    public async Task SeparateDeniedReplyAndQuarantineCommitWithoutReservingTheLegitimateCanonicalIdentity()
    {
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync();
        MessageProcessingAttempt attempt = Request();
        Guid rejectionId = Guid.NewGuid();
        await using (ILocalTransaction owner = await fixture.Factory.BeginAsync("Messaging"))
        {
            await fixture.Store.RecordRejectionAsync(owner.Context, Rejection(attempt, rejectionId));
            MessageOutgoingRecord rejectedReply = Reply(attempt, rejectionId);
            await fixture.Store.StageAsync(owner.Context, rejectedReply);
            Assert.IsTrue(await fixture.Store.AttachReplyAsync(owner.Context, new(rejectedReply.RecordId, RejectionId: rejectionId)));
            Assert.AreEqual(TransactionOutcome.Committed, (await owner.CommitAsync()).Outcome);
        }
        Assert.AreEqual(0L, await fixture.CountAsync("processing"));
        Assert.AreEqual(1L, await fixture.CountAsync("rejections"));
        await using (ILocalTransaction legitimate = await fixture.Factory.BeginAsync("Messaging"))
        {
            MessageProcessingClaim claim = await fixture.Store.ClaimProcessingAsync(legitimate.Context, attempt);
            Assert.AreEqual(MessageClaimDisposition.Acquired, claim.Disposition);
            await fixture.InsertBusinessAsync(legitimate.Context, attempt.Key.MessageId);
            MessageOutgoingRecord legitimateReply = Reply(attempt);
            await fixture.Store.StageAsync(legitimate.Context, legitimateReply);
            Assert.IsTrue(await fixture.Store.CompleteProcessingAsync(legitimate.Context,
                new(attempt.Key, claim.Record!.Version, 1, ReplyRecordId: legitimateReply.RecordId)));
            await legitimate.CommitAsync();
        }
        Assert.AreEqual(1L, await fixture.CountAsync("business_record"));
        Assert.AreEqual(1L, await fixture.CountAsync("processing"));
        Assert.AreEqual(3L, await fixture.CountAsync("outbox"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FiniteCleanupKeepsAnUnresolvedRejectedReplyAndDetachesOnlyResolvedTombstones(bool expiredReply)
    {
        var policy = new MessageRetentionPolicy(TimeSpan.FromTicks(1), terminalRetention: TimeSpan.FromSeconds(1),
            supportedRedriveHorizon: TimeSpan.FromTicks(1));
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync(policy);
        MessageProcessingAttempt attempt = Request();
        Guid rejectionId = Guid.NewGuid();
        await using (ILocalTransaction owner = await fixture.Factory.BeginAsync("Messaging"))
        {
            await fixture.Store.RecordRejectionAsync(owner.Context, Rejection(attempt, rejectionId));
            MessageOutgoingRecord reply = Reply(attempt, rejectionId);
            await fixture.Store.StageAsync(owner.Context, reply);
            Assert.IsTrue(await fixture.Store.AttachReplyAsync(owner.Context, new(reply.RecordId, RejectionId: rejectionId)));
            await owner.CommitAsync();
        }
        MessageReservation[] reservations = await fixture.ReserveCommittedAsync();
        await fixture.PublishConfirmedAsync(reservations.Single(row => row.Purpose == MessageRecordPurpose.Rejection));
        await fixture.SqlAsync($"UPDATE {fixture.Table("rejections")} SET terminal_expires=clock_timestamp()-INTERVAL '1 second'; UPDATE {fixture.Table("outbox")} SET payload_expires=clock_timestamp()-INTERVAL '1 second'");
        Assert.AreEqual(new MessageMaintenanceCounts(0, 0, 0), await MaintainAsync(fixture));
        Assert.AreEqual(1L, await fixture.CountAsync("rejections"));
        await using (ILocalTransaction settle = await fixture.Factory.BeginAsync("Messaging"))
        {
            MessagePublicationResult result = expiredReply
                ? new(MessagePublicationDisposition.Rejected, MessagePublicationFailure.ReplyDestinationExpired)
                : new(MessagePublicationDisposition.Confirmed);
            Assert.IsTrue(await fixture.Store.RecordPublicationAsync(settle.Context,
                new(reservations.Single(row => row.Purpose == MessageRecordPurpose.Reply), result)));
            await settle.CommitAsync();
        }
        Assert.AreEqual(1, (await MaintainAsync(fixture)).TerminalDeleted);
        Assert.AreEqual(0L, await fixture.CountAsync("rejections"));
        Assert.AreEqual(2, (await MaintainAsync(fixture)).PayloadsCleared);
        Assert.AreEqual(2L, await fixture.CountAsync("outbox"));
        await using DbConnection connection = await fixture.Source.OpenConnectionAsync();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM {fixture.Table("outbox")} WHERE purpose=3 AND rejection_id IS NULL AND envelope IS NULL";
        Assert.AreEqual(1L, (long)(await command.ExecuteScalarAsync())!);
    }

    [TestMethod]
    public async Task RedriveClearsTheOldReplyAndAllowsOneFreshReplyInTheNewGeneration()
    {
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync();
        MessageProcessingAttempt initial = Request();
        Guid oldReply;
        await using (ILocalTransaction owner = await fixture.Factory.BeginAsync("Messaging"))
        {
            MessageDispositionResult terminal = await TerminalAsync(fixture, owner.Context, initial);
            MessageOutgoingRecord reply = Reply(initial);
            oldReply = reply.RecordId;
            await fixture.Store.StageAsync(owner.Context, reply);
            Assert.IsTrue(await fixture.Store.AttachReplyAsync(owner.Context, new(reply.RecordId, initial.Key, terminal.Record!.Version, 1)));
            await owner.CommitAsync();
        }
        MessageRedriveResult redriven;
        await using (ILocalTransaction owner = await fixture.Factory.BeginAsync("Messaging"))
        {
            MessageProcessingRecord retained = (await fixture.Store.ReadProcessingAsync(owner.Context, initial.Key))!;
            redriven = await fixture.Store.RedriveAsync(owner.Context, new(initial.Key, retained.Version, new("CountryEvents"), null, "operator.reviewed"));
            Assert.AreEqual(MessageRedriveDisposition.Prepared, redriven.Disposition);
            Assert.IsNull(redriven.Record!.ReplyRecordId);
            await owner.CommitAsync();
        }
        MessageProcessingAttempt retry = initial with { Generation = 1, Purpose = MessageRecordPurpose.Redrive, HandoffRecordId = redriven.HandoffRecordId };
        await using (ILocalTransaction failed = await fixture.Factory.BeginAsync("Messaging"))
        {
            Assert.AreEqual(MessageClaimDisposition.Acquired, (await fixture.Store.ClaimProcessingAsync(failed.Context, retry)).Disposition);
            await failed.RollbackAsync();
        }
        await using (ILocalTransaction owner = await fixture.Factory.BeginAsync("Messaging"))
        {
            MessageDispositionResult terminal = await TerminalAsync(fixture, owner.Context, retry);
            MessageOutgoingRecord reply = Reply(retry);
            Assert.AreNotEqual(oldReply, reply.RecordId);
            await fixture.Store.StageAsync(owner.Context, reply);
            Assert.IsTrue(await fixture.Store.AttachReplyAsync(owner.Context,
                new(reply.RecordId, retry.Key, terminal.Record!.Version, 1, Generation: 1)));
            await owner.CommitAsync();
        }
        Assert.AreEqual(5L, await fixture.CountAsync("outbox"));
    }

    [TestMethod]
    public async Task MissingRequiredReplyAttachmentRollsBackFreshTerminalAndBothOutgoingRowsTogether()
    {
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync();
        LocalApplicationIdentity identity = LocalApplicationIdentity.Capture();
        MessageProcessingAttempt request = Request();
        MessageProcessingAttempt attempt = request with
        {
            Key = new(identity.Application, identity.Environment, request.Key.Subscription, request.Key.MessageId)
        };
        var codec = new ReplyNumberCodec("country.updated");
        var definition = new MessageRequestDefinition<int, int>("Query", "CountryEvents", codec, new ReplyNumberCodec("country.reply"),
            MessageRequestMode.Query, identity.Application, identity.Environment);
        var subscription = new MessageRequestSubscriptionDefinition<int, int>("Countries", "Messaging", definition,
            _ => throw new InvalidOperationException("Bookkeeping cannot invoke the handler."),
            (_, _, _, _) => ValueTask.FromResult(new CapabilityTarget(identity.Application, identity.Environment)), ["fixture.handle"]);
        var snapshot = new MessagingSnapshot(new MessagingOptions(), [new MessageRouteDefinition<int>("CountryEvents", MessageKind.Command, codec)],
            [new("Messaging", "Messaging")], [subscription], [definition]);
        TransactionCompletion rolledBack;
        await using (ILocalTransaction original = await fixture.Factory.BeginAsync("Messaging"))
        {
            Assert.AreEqual(MessageClaimDisposition.Acquired, (await fixture.Store.ClaimProcessingAsync(original.Context, attempt)).Disposition);
            rolledBack = await original.RollbackAsync();
        }
        var coordinator = new MessageDeliveryDispositionCoordinator(snapshot, fixture.Store, fixture.Factory, identity);
        MessageDispositionHandling result = await coordinator.HandleAsync(new(attempt.Message, subscription,
            MessageDeliveryFailureKind.CanonicalTerminal, "messaging.terminal", attempt, true, rolledBack,
            Reply: async (context, token) =>
            {
                await fixture.Store.StageAsync(context, Reply(attempt), token);
                return Guid.NewGuid(); // A programming fault: not the actual staged reply identity.
            }));
        Assert.AreEqual(MessageDispositionHandlingKind.Stop, result.Kind);
        Assert.AreEqual(TransactionOutcome.NotCommitted, result.Completion!.Outcome);
        Assert.IsInstanceOfType<InvalidDataException>(result.Fault!.SourceException);
        Assert.AreEqual(0L, await fixture.CountAsync("processing"));
        Assert.AreEqual(0L, await fixture.CountAsync("outbox"));
    }

    [TestMethod]
    public async Task ExplicitSchemaV2RejectsAnOldVersionWithoutInstallingOrRepairingIt()
    {
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync();
        await using (DbConnection installed = await fixture.Source.OpenConnectionAsync())
        {
            await using DbCommand version = installed.CreateCommand();
            version.CommandText = $"SELECT version FROM {fixture.Table("schema_version")}";
            Assert.AreEqual(CIS.Messaging.PostgreSql.PostgreSqlMessagingSchema.Version, (int)(await version.ExecuteScalarAsync())!);
        }
        await fixture.SqlAsync($"ALTER TABLE {fixture.Table("schema_version")} DROP CONSTRAINT schema_version_version_check; UPDATE {fixture.Table("schema_version")} SET version=1");
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Messaging");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Store.ReserveAsync(owner.Context,
            new(PostgresMessageFixture.Application, PostgresMessageFixture.EnvironmentName, Guid.NewGuid())));
        await owner.RollbackAsync();
        await using DbConnection connection = await fixture.Source.OpenConnectionAsync();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT version FROM {fixture.Table("schema_version")}";
        Assert.AreEqual(1, (int)(await command.ExecuteScalarAsync())!);
    }

    private static MessageProcessingAttempt Request()
    {
        EncodedMessage request = MessageWire.Encode(new(Guid.NewGuid(), MessageKind.Request, new("country.updated", 1),
            "producer", PostgresMessageFixture.EnvironmentName, DateTimeOffset.UnixEpoch, "1"u8.ToArray(),
            requestAttemptId: Guid.NewGuid(), replyRoute: new("producer", PostgresMessageFixture.EnvironmentName, Guid.NewGuid())));
        return PostgresMessageFixture.Attempt(request);
    }

    private static MessageOutgoingRecord Reply(MessageProcessingAttempt attempt, Guid? rejectionId = null)
    {
        EncodedMessage reply = MessageWire.Encode(new(Guid.NewGuid(), MessageKind.Reply, new("country.reply", 1),
            attempt.Key.Application, attempt.Key.Environment, DateTimeOffset.UnixEpoch, "null"u8.ToArray(),
            requestAttemptId: attempt.Message.Envelope.RequestAttemptId, inReplyToMessageId: attempt.Key.MessageId));
        return new(Guid.NewGuid(), attempt.Key.Application, attempt.Key.Environment,
            new("CountryEvents", attempt.Message.Envelope.ReplyRoute), reply, MessageRecordPurpose.Reply,
            ProcessingKey: rejectionId is null ? attempt.Key : null, DeliveryOrdinal: rejectionId is null ? attempt.DeliveryOrdinal : 1,
            Generation: rejectionId is null ? attempt.Generation : 0, RejectionId: rejectionId);
    }

    private static MessageRejection Rejection(MessageProcessingAttempt attempt, Guid id)
    {
        var quarantine = new MessageOutgoingRecord(Guid.NewGuid(), attempt.Key.Application, attempt.Key.Environment,
            new("CountryEvents"), attempt.Message, MessageRecordPurpose.Rejection, DiagnosticCode: "messaging.operation_denied",
            OriginalProvenance: attempt.Provenance, TargetSubscription: attempt.Key.Subscription);
        return new(id, attempt.Key.Application, attempt.Key.Environment, attempt.Key.Subscription, attempt.Message,
            "messaging.operation_denied", quarantine, attempt.Provenance);
    }

    private static async Task<MessageDispositionResult> TerminalAsync(PostgresMessageFixture fixture, ILocalTransactionContext context, MessageProcessingAttempt attempt)
    {
        MessageDispositionResult result = await fixture.Store.SecureDispositionAsync(context,
            new(attempt, MessageProcessingDisposition.Terminal, PostgresMessageFixture.Handoff(attempt, terminal: true), "messaging.terminal"));
        Assert.AreEqual(MessageDispositionResultKind.Secured, result.Disposition);
        return result;
    }

    private static async Task<MessageMaintenanceCounts> MaintainAsync(PostgresMessageFixture fixture)
    {
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Messaging");
        MessageMaintenanceCounts counts = await fixture.Store.MaintainAsync(owner.Context,
            new(PostgresMessageFixture.Application, PostgresMessageFixture.EnvironmentName, fixture.Policy));
        await owner.CommitAsync();
        return counts;
    }

    private sealed class ReplyNumberCodec(string name) : IMessageCodec<int>
    {
        public MessageContract Contract { get; } = new(name, 1);
        public byte[] Encode(int value) => JsonSerializer.SerializeToUtf8Bytes(value);
        public bool TryDecode(MessageContract contract, ReadOnlyMemory<byte> json, out int value)
        {
            value = JsonSerializer.Deserialize<int>(json.Span);
            return contract == Contract;
        }
    }
}

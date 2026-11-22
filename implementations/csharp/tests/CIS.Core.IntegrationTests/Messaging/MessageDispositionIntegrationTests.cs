using System.Text.Json;
using CIS.Messaging;
using CIS.Security;
using CIS.Transactions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Messaging;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("RabbitMq")]
[DoNotParallelize]
public sealed class MessageDispositionIntegrationTests
{
    [TestMethod]
    public async Task RealRollbackThenFreshDispositionSecuresOneChainAndFailedRetryRetainsItsCurrentOrdinal()
    {
        await using PostgresMessageFixture database = await PostgresMessageFixture.CreateAsync();
        LocalApplicationIdentity identity = LocalApplicationIdentity.Capture();
        MessagingSnapshot snapshot = Snapshot(database);
        MessageProcessingAttempt attempt = Attempt(identity);
        TransactionCompletion original = await RollbackAttemptAsync(database, attempt);
        var coordinator = new MessageDeliveryDispositionCoordinator(snapshot, database.Store, database.Factory, identity, jitter: () => 0.5);
        MessageFailedDelivery failed = Failure(snapshot, attempt, original);
        Assert.AreEqual(MessageDispositionHandlingKind.ResponsibilityCommitted, (await coordinator.HandleAsync(failed)).Kind);
        MessageProcessingRecord first = await ReadAsync(database, attempt.Key);
        Assert.AreEqual(MessageProcessingDisposition.RetryScheduled, first.Disposition);
        Assert.AreEqual(2, first.DeliveryOrdinal);
        Assert.AreEqual(0L, await database.CountAsync("business_record"));
        Assert.AreEqual(1L, await database.CountAsync("outbox"));
        Assert.AreEqual(MessageDispositionHandlingKind.ResponsibilityCommitted, (await coordinator.HandleAsync(failed)).Kind);
        Assert.AreEqual(1L, await database.CountAsync("outbox"), "A repeated failed callback cannot create a second retry chain.");

        MessageProcessingAttempt retry = attempt with
        {
            DeliveryOrdinal = 2,
            Purpose = MessageRecordPurpose.Retry,
            HandoffRecordId = first.HandoffRecordId
        };
        TransactionCompletion retryRollback = await RollbackAttemptAsync(database, retry);
        MessageProcessingRecord restored = await ReadAsync(database, retry.Key);
        Assert.AreEqual(MessageProcessingDisposition.RetryScheduled, restored.Disposition);
        Assert.AreEqual(2, restored.DeliveryOrdinal);
        Assert.AreEqual(MessageDispositionHandlingKind.ResponsibilityCommitted,
            (await coordinator.HandleAsync(Failure(snapshot, retry, retryRollback))).Kind);
        Assert.AreEqual(3, (await ReadAsync(database, retry.Key)).DeliveryOrdinal);
        Assert.AreEqual(2L, await database.CountAsync("outbox"));
        Assert.AreEqual(0L, await database.CountAsync("business_record"));
    }

    [TestMethod]
    public async Task ARealCommittedSuccessInTheRollbackGapIsNeverOverwrittenByFailureBookkeeping()
    {
        await using PostgresMessageFixture database = await PostgresMessageFixture.CreateAsync();
        LocalApplicationIdentity identity = LocalApplicationIdentity.Capture();
        MessagingSnapshot snapshot = Snapshot(database);
        MessageProcessingAttempt attempt = Attempt(identity);
        TransactionCompletion rolledBack = await RollbackAttemptAsync(database, attempt);
        await using (ILocalTransaction winner = await database.Factory.BeginAsync("Messaging"))
        {
            MessageProcessingClaim claim = await database.Store.ClaimProcessingAsync(winner.Context, attempt);
            Assert.AreEqual(MessageClaimDisposition.Acquired, claim.Disposition);
            await database.InsertBusinessAsync(winner.Context, attempt.Key.MessageId);
            Assert.IsTrue(await database.Store.CompleteProcessingAsync(winner.Context, new(attempt.Key, claim.Record!.Version, 1)));
            Assert.AreEqual(TransactionOutcome.Committed, (await winner.CommitAsync()).Outcome);
        }
        var coordinator = new MessageDeliveryDispositionCoordinator(snapshot, database.Store, database.Factory, identity);
        MessageDispositionHandling observed = await coordinator.HandleAsync(Failure(snapshot, attempt, rolledBack));
        Assert.AreEqual(MessageDispositionHandlingKind.ResponsibilityCommitted, observed.Kind);
        Assert.AreEqual(MessageProcessingDisposition.Handled, observed.Disposition);
        Assert.AreEqual(1L, await database.CountAsync("business_record"));
        Assert.AreEqual(0L, await database.CountAsync("outbox"));
        Assert.AreEqual(MessageProcessingDisposition.Handled, (await ReadAsync(database, attempt.Key)).Disposition);
    }

    [TestMethod]
    public async Task LostBookkeepingCommitReplyLeavesActualDurableResponsibilityAndARepeatResolvesWithoutAnotherHandoff()
    {
        await using PostgresMessageFixture database = await PostgresMessageFixture.CreateAsync();
        LocalApplicationIdentity identity = LocalApplicationIdentity.Capture();
        MessagingSnapshot snapshot = Snapshot(database);
        MessageProcessingAttempt attempt = Attempt(identity);
        TransactionCompletion original = await RollbackAttemptAsync(database, attempt);
        var coordinator = new MessageDeliveryDispositionCoordinator(snapshot, database.Store, database.Factory, identity);
        database.Boundary.FailAfterNextCommit();
        MessageDispositionHandling uncertain = await coordinator.HandleAsync(Failure(snapshot, attempt, original));
        Assert.AreEqual(MessageDispositionHandlingKind.Stop, uncertain.Kind);
        Assert.AreEqual(TransactionOutcome.Unknown, uncertain.Completion!.Outcome);
        Assert.AreEqual(1L, await database.CountAsync("outbox"));
        MessageDispositionHandling resolved = await coordinator.HandleAsync(Failure(snapshot, attempt, original));
        Assert.AreEqual(MessageDispositionHandlingKind.ResponsibilityCommitted, resolved.Kind);
        Assert.AreEqual(TransactionOutcome.Committed, resolved.Completion!.Outcome);
        Assert.AreEqual(1L, await database.CountAsync("outbox"));
        Assert.AreEqual(0L, await database.CountAsync("business_record"));
    }

    [TestMethod]
    public async Task RejectionIsCanonicalFreeAndAuthorizedRedriveUsesTheInspectedTerminalVersion()
    {
        await using PostgresMessageFixture database = await PostgresMessageFixture.CreateAsync();
        LocalApplicationIdentity identity = LocalApplicationIdentity.Capture();
        MessagingSnapshot snapshot = Snapshot(database);
        MessageProcessingAttempt attempt = Attempt(identity);
        var coordinator = new MessageDeliveryDispositionCoordinator(snapshot, database.Store, database.Factory, identity);
        MessageDispositionHandling rejected = await coordinator.HandleAsync(new(attempt.Message, snapshot.Subscriptions.Single(),
            MessageDeliveryFailureKind.Rejected, "messaging.schema_rejected"));
        Assert.AreEqual(MessageDispositionHandlingKind.ResponsibilityCommitted, rejected.Kind);
        Assert.AreEqual(0L, await database.CountAsync("processing"));
        Assert.AreEqual(1L, await database.CountAsync("rejections"));
        TransactionCompletion original = await RollbackAttemptAsync(database, attempt);
        Assert.AreEqual(MessageProcessingDisposition.Terminal,
            (await coordinator.HandleAsync(Failure(snapshot, attempt, original) with
            {
                Kind = MessageDeliveryFailureKind.CanonicalTerminal
            })).Disposition);

        await using var resources = new MessagingResourceBudget(snapshot.Settings);
        var authority = new DispositionIntegrationAuthority();
        var admin = new MessageAdministration(snapshot, database.Store, database.Factory, identity, authority, authority, authority, resources);
        MessageInspectResult inspected = await admin.InspectAsync(new(subscription: "Countries", messageId: attempt.Key.MessageId));
        Assert.AreEqual(MessageProcessingDisposition.Terminal, inspected.Processing!.Disposition);
        long expected = inspected.Processing.Version;
        MessageAdminRedriveResult redrive = await admin.RedriveAsync(new("Countries", attempt.Key.MessageId, expected, "operator.reviewed"));
        Assert.AreEqual(TransactionOutcome.Committed, redrive.Completion!.Outcome);
        Assert.AreEqual(MessageRedriveDisposition.Prepared, redrive.Transition);
        Assert.AreEqual(1, redrive.Generation);
        MessageAdminRedriveResult stale = await admin.RedriveAsync(new("Countries", attempt.Key.MessageId, expected, "operator.reviewed"));
        Assert.AreEqual(MessageRedriveDisposition.Conflict, stale.Transition);
        MessageAdminMaintenanceResult maintenance = await admin.MaintainAsync(16);
        Assert.AreEqual(new MessageMaintenanceCounts(0, 0, 0), maintenance.ConfirmedCounts);
        Assert.AreEqual(3L, await database.CountAsync("outbox"), "Indefinite retention cannot clear rejection, terminal or pending redrive responsibility.");
        Assert.AreEqual(1L, await database.CountAsync("processing"));
        Assert.IsNotNull(authority.LastAudit);
    }

    private static MessagingSnapshot Snapshot(PostgresMessageFixture database)
    {
        var codec = new DispositionIntegrationCodec();
        var subscription = new MessageSubscriptionDefinition<int>("Countries", "Messaging", "CountryEvents", codec,
            _ => throw new InvalidOperationException("Failure bookkeeping must not run a handler."),
            (_, _, _, _) => throw new InvalidOperationException("No resource resolver belongs to bookkeeping."), ["country.read"],
            new()
            {
                MaximumDeliveryOrdinals = 3,
                RetryDelays = [TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2)],
                RetryJitterRatio = 0
            });
        return new(new MessagingOptions
        {
            Retention = database.Policy,
            InspectionCapabilities = ["messaging.inspect"],
            MaintenanceCapabilities = ["messaging.maintain"]
        }, [new MessageRouteDefinition<int>("CountryEvents", MessageKind.Event, codec)], [new("Messaging", "Messaging")], [subscription]);
    }

    private static MessageProcessingAttempt Attempt(LocalApplicationIdentity identity)
    {
        EncodedMessage message = PostgresMessageFixture.Message();
        return new(new(identity.Application, identity.Environment, "Countries", message.Envelope.MessageId), message, PostgresMessageFixture.Provenance());
    }

    private static MessageFailedDelivery Failure(MessagingSnapshot snapshot, MessageProcessingAttempt attempt, TransactionCompletion completion)
        => new(attempt.Message, snapshot.Subscriptions.Single(), MessageDeliveryFailureKind.HandlerFault,
            "messaging.handler_failed", attempt, true, completion);

    private static async Task<TransactionCompletion> RollbackAttemptAsync(PostgresMessageFixture database, MessageProcessingAttempt attempt)
    {
        await using ILocalTransaction owner = await database.Factory.BeginAsync("Messaging");
        MessageProcessingClaim claim = await database.Store.ClaimProcessingAsync(owner.Context, attempt);
        Assert.AreEqual(MessageClaimDisposition.Acquired, claim.Disposition);
        await database.InsertBusinessAsync(owner.Context, attempt.Key.MessageId);
        return await owner.RollbackAsync();
    }

    private static async Task<MessageProcessingRecord> ReadAsync(PostgresMessageFixture database, MessageProcessingKey key)
    {
        await using ILocalTransaction owner = await database.Factory.BeginAsync("Messaging");
        MessageProcessingRecord? record = await database.Store.ReadProcessingAsync(owner.Context, key);
        await owner.RollbackAsync();
        Assert.IsNotNull(record);
        return record;
    }
}

internal sealed class DispositionIntegrationCodec : IMessageCodec<int>
{
    public MessageContract Contract { get; } = new("country.updated", 1);
    public byte[] Encode(int value) => JsonSerializer.SerializeToUtf8Bytes(new { value });
    public bool TryDecode(MessageContract contract, ReadOnlyMemory<byte> json, out int value)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        value = document.RootElement.GetProperty("value").GetInt32();
        return contract == Contract;
    }
}

// SQL orchestration proof uses a deterministic authorizer; real native authentication remains the dispatcher/provider acceptance.
internal sealed class DispositionIntegrationAuthority : ICapabilityAuthorizer, ICallerContextAccessor, ISecurityAuditSink
{
    internal SecurityOperationAudit? LastAudit;
    public CallerContext Current
    {
        get;
    } = new("Native", "Native", new("fixture", "operator"), SecurityActorKind.User,
        null, null, new(DateTimeOffset.UnixEpoch), "fixture-v1");
    public ValueTask<AuthorizationDecision> AuthorizeAsync(IReadOnlyList<string> capabilities, CapabilityTarget target, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(AuthorizationDecision.Allow);
    public ValueTask AuthorizationCompletedAsync(SecurityAuthorizationAudit audit, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask OperationCompletedAsync(SecurityOperationAudit audit, CancellationToken cancellationToken = default)
    {
        LastAudit = audit;
        return ValueTask.CompletedTask;
    }
}

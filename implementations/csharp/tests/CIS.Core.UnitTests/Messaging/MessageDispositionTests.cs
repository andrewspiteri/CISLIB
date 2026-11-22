using System.Runtime.ExceptionServices;
using CIS.CommonTesting;
using CIS.Messaging;
using CIS.Security;
using CIS.Transactions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Messaging;

[TestClass]
[DoNotParallelize]
public sealed class MessageDispositionTests
{
    [TestMethod]
    [DataRow(TransactionOutcome.Committed)]
    [DataRow(TransactionOutcome.NotCommitted)]
    [DataRow(TransactionOutcome.Unknown)]
    public async Task AcknowledgementEligibilityRequiresTheFreshOwnersActualCommittedSettlement(TransactionOutcome outcome)
    {
        await using var fixture = new DispositionUnitFixture();
        fixture.Script(outcome);
        MessageFailedDelivery failure = fixture.Failure();
        MessageDispositionHandling result = await fixture.Coordinator.HandleAsync(failure);
        Assert.AreEqual(outcome == TransactionOutcome.Committed ? MessageDispositionHandlingKind.ResponsibilityCommitted
            : MessageDispositionHandlingKind.Stop, result.Kind);
        Assert.AreEqual(outcome, result.Completion!.Outcome);
        Assert.AreEqual(outcome == TransactionOutcome.Committed ? 1 : 0, fixture.Wakes);
        Assert.IsTrue(fixture.Transactions.CreatedTransactions.Single().DisposeRequested);
        Assert.AreSame(failure.Message, fixture.Store.Disposition!.Handoff.Message);
        Assert.AreSame(failure.Attempt, fixture.Store.Disposition.Attempt);
        Assert.AreEqual(2, fixture.Store.Disposition.Handoff.DeliveryOrdinal);
        Assert.AreEqual("Countries", fixture.Store.Disposition.Handoff.TargetSubscription);
        Assert.AreEqual(TimeSpan.FromSeconds(1), fixture.Store.Disposition.Handoff.Delay);
    }

    [TestMethod]
    [DataRow((int)MessageDeliveryFailureKind.PersistenceUnknown)]
    [DataRow((int)MessageDeliveryFailureKind.InfrastructureUnavailable)]
    public async Task UncertainPersistenceAndUnadmittedInfrastructureNeverConsumeAHandlerOrdinal(int kind)
    {
        await using var fixture = new DispositionUnitFixture();
        MessageDispositionHandling result = await fixture.Coordinator.HandleAsync(fixture.Failure((MessageDeliveryFailureKind)kind));
        Assert.AreEqual(MessageDispositionHandlingKind.Stop, result.Kind);
        Assert.AreEqual(0, fixture.Transactions.CreatedTransactions.Count);
        Assert.AreEqual(0, fixture.Store.Calls);
    }

    [TestMethod]
    public async Task OriginalUnknownAndCommittedOwnersNeverBecomeFreshRetryResponsibility()
    {
        await using var fixture = new DispositionUnitFixture();
        foreach (TransactionOutcome outcome in new[] { TransactionOutcome.Unknown, TransactionOutcome.Committed })
        {
            MessageDispositionHandling result = await fixture.Coordinator.HandleAsync(fixture.Failure() with
            {
                OriginalCompletion = DispositionUnitFixture.Completion(outcome)
            });
            Assert.AreEqual(MessageDispositionHandlingKind.Stop, result.Kind);
        }
        Assert.AreEqual(0, fixture.Store.Calls);
        Assert.AreEqual(0, fixture.Transactions.CreatedTransactions.Count);
    }

    [TestMethod]
    [DataRow(1, (int)MessageDeliveryFailureKind.CanonicalTerminal)]
    [DataRow(3, (int)MessageDeliveryFailureKind.HandlerFault)]
    public async Task ExplicitTerminalAndExhaustedBudgetSecureTheSameOrdinalWithoutSchedulingAnotherAttempt(int ordinal, int kind)
    {
        await using var fixture = new DispositionUnitFixture();
        fixture.Script(TransactionOutcome.Committed);
        MessageDispositionHandling result = await fixture.Coordinator.HandleAsync(fixture.Failure((MessageDeliveryFailureKind)kind, ordinal));
        Assert.AreEqual(MessageDispositionHandlingKind.ResponsibilityCommitted, result.Kind);
        Assert.AreEqual(MessageProcessingDisposition.Terminal, result.Disposition);
        Assert.AreEqual(ordinal, fixture.Store.Disposition!.Handoff.DeliveryOrdinal);
        Assert.AreEqual(MessageRecordPurpose.Terminal, fixture.Store.Disposition.Handoff.Purpose);
        Assert.AreEqual(TimeSpan.Zero, fixture.Store.Disposition.Handoff.Delay);
        Assert.AreEqual(ordinal == 3 ? "messaging.retry.exhausted" : "messaging.handler_failed", fixture.Store.Disposition.SafeCode);
    }

    [TestMethod]
    [DataRow(MessageDispositionResultKind.AlreadyHandled)]
    [DataRow(MessageDispositionResultKind.AlreadySecured)]
    public async Task ACommittedRollbackGapWinnerSuppressesNewReplyAndPreservesTheObservedDisposition(MessageDispositionResultKind winner)
    {
        await using var fixture = new DispositionUnitFixture();
        fixture.Script(TransactionOutcome.Committed);
        fixture.Store.Winner = winner;
        int replyCalls = 0;
        MessageDispositionHandling result = await fixture.Coordinator.HandleAsync(fixture.Failure(MessageDeliveryFailureKind.CanonicalTerminal) with
        {
            Reply = (_, _) =>
            {
                replyCalls++;
                return Task.FromResult(Guid.NewGuid());
            }
        });
        Assert.AreEqual(MessageDispositionHandlingKind.ResponsibilityCommitted, result.Kind);
        Assert.AreEqual(winner == MessageDispositionResultKind.AlreadyHandled ? MessageProcessingDisposition.Handled
            : MessageProcessingDisposition.Terminal, result.Disposition);
        Assert.AreEqual(0, replyCalls);
        Assert.AreEqual(0, fixture.Store.Rejections);
    }

    [TestMethod]
    [DataRow(MessageDispositionResultKind.ContentConflict)]
    [DataRow(MessageDispositionResultKind.ProvenanceConflict)]
    [DataRow(MessageDispositionResultKind.InvalidOrdinal)]
    public async Task ConflictingFailedCandidatesRollbackBeforeOneSeparateCanonicalFreeRejection(MessageDispositionResultKind conflict)
    {
        await using var fixture = new DispositionUnitFixture();
        fixture.Script(TransactionOutcome.Committed, TransactionOutcome.Committed);
        fixture.Store.Winner = conflict;
        fixture.Store.OnRejection = () =>
        {
            Assert.IsTrue(fixture.Transactions.CreatedTransactions[0].DisposeRequested);
            Assert.AreEqual(TransactionOutcome.NotCommitted, fixture.Transactions.CreatedTransactions[0].Completion!.Outcome);
        };
        MessageDispositionHandling result = await fixture.Coordinator.HandleAsync(fixture.Failure());
        Assert.AreEqual(MessageDispositionHandlingKind.ResponsibilityCommitted, result.Kind);
        Assert.AreEqual(2, fixture.Transactions.CreatedTransactions.Count);
        Assert.AreEqual(1, fixture.Store.Rejections);
        Assert.IsNull(fixture.Store.Rejection!.Quarantine.ProcessingKey);
        Assert.AreEqual("Countries", fixture.Store.Rejection.Quarantine.TargetSubscription);
        Assert.AreEqual(MessageRecordPurpose.Rejection, fixture.Store.Rejection.Quarantine.Purpose);
    }

    [TestMethod]
    public async Task BookkeepingFaultsAwaitRollbackAndKeepTheOriginalHandlerFaultWithoutPersistingExceptionText()
    {
        await using var fixture = new DispositionUnitFixture();
        fixture.Script(TransactionOutcome.Committed);
        var original = new InvalidOperationException("private-handler-detail");
        fixture.Store.Fault = new IOException("private-storage-detail");
        MessageDispositionHandling result = await fixture.Coordinator.HandleAsync(fixture.Failure() with { OriginalFault = ExceptionDispatchInfo.Capture(original) });
        Assert.AreEqual(MessageDispositionHandlingKind.Stop, result.Kind);
        Assert.AreSame(original, result.Fault!.SourceException);
        Assert.AreEqual(TransactionOutcome.NotCommitted, result.Completion!.Outcome);
        Assert.AreEqual("messaging.handler_failed", fixture.Store.Disposition!.SafeCode);
        Assert.IsTrue(fixture.Transactions.CreatedTransactions.Single().DisposeRequested);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ARequiredTerminalReplyMustAttachUnderTheFreshOwnersExactVersionBeforeCommit(bool attach)
    {
        await using var fixture = new DispositionUnitFixture();
        fixture.Script(TransactionOutcome.Committed);
        fixture.Store.AttachResult = attach;
        Guid replyId = Guid.NewGuid();
        MessageFailedDelivery failed = fixture.Failure(MessageDeliveryFailureKind.CanonicalTerminal) with
        {
            Reply = (context, _) =>
            {
                Assert.AreSame(fixture.Store.Context, context);
                return Task.FromResult(replyId);
            }
        };
        MessageDispositionHandling result = await fixture.Coordinator.HandleAsync(failed);
        Assert.AreEqual(attach ? MessageDispositionHandlingKind.ResponsibilityCommitted : MessageDispositionHandlingKind.Stop, result.Kind);
        Assert.AreEqual(attach ? TransactionOutcome.Committed : TransactionOutcome.NotCommitted, result.Completion!.Outcome);
        Assert.AreEqual(replyId, fixture.Store.Attachment!.ReplyRecordId);
        Assert.AreEqual(failed.Attempt!.Key, fixture.Store.Attachment.ProcessingKey);
        Assert.AreEqual(2L, fixture.Store.Attachment.ExpectedVersion);
        Assert.AreEqual(1, fixture.Store.Attachment.ExpectedOrdinal);
        Assert.IsNull(fixture.Store.Attachment.RejectionId);
    }

    [TestMethod]
    [DataRow("messaging.handler_denied", true)]
    [DataRow("messaging.operation_denied", true)]
    [DataRow("messaging.producer_untrusted", false)]
    [DataRow("messaging.delivery_metadata_invalid", false)]
    public async Task OnlyExplicitPrevalidatedDenialsCanAttachACanonicalFreeSafeReply(string code, bool permitted)
    {
        await using var fixture = new DispositionUnitFixture();
        fixture.Script(TransactionOutcome.Committed);
        int replyCalls = 0;
        MessageDispositionHandling result = await fixture.Coordinator.HandleAsync(fixture.Failure(MessageDeliveryFailureKind.Rejected) with
        {
            SafeCode = code,
            CanonicalEligible = false,
            DeniedReply = (context, rejectionId, _) =>
            {
                replyCalls++;
                Assert.AreSame(fixture.Store.Context, context);
                Assert.AreEqual(fixture.Store.Rejection!.RejectionId, rejectionId);
                return Task.FromResult(Guid.NewGuid());
            }
        });
        Assert.AreEqual(MessageDispositionHandlingKind.ResponsibilityCommitted, result.Kind);
        Assert.AreEqual(permitted ? 1 : 0, replyCalls);
        Assert.IsNull(fixture.Store.Disposition);
        if (permitted)
        {
            Assert.AreEqual(fixture.Store.Rejection!.RejectionId, fixture.Store.Attachment!.RejectionId);
            Assert.IsNull(fixture.Store.Attachment.ProcessingKey);
        }
        else
        {
            Assert.IsNull(fixture.Store.Attachment);
        }
    }

    [TestMethod]
    public async Task ARequiredTerminalReplyFaultRollsBackTheWholeFreshResponsibilityOwner()
    {
        await using var fixture = new DispositionUnitFixture();
        fixture.Script(TransactionOutcome.Committed);
        var original = new InvalidOperationException("reply-stage-fault");
        MessageDispositionHandling result = await fixture.Coordinator.HandleAsync(fixture.Failure(MessageDeliveryFailureKind.CanonicalTerminal) with
        {
            Reply = (context, _) =>
            {
                Assert.AreSame(fixture.Store.Context, context);
                return Task.FromException<Guid>(original);
            }
        });
        Assert.AreEqual(MessageDispositionHandlingKind.Stop, result.Kind);
        Assert.AreSame(original, result.Fault!.SourceException);
        Assert.AreEqual(TransactionOutcome.NotCommitted, result.Completion!.Outcome);
        Assert.AreEqual(0, fixture.Wakes);
    }
}

// These bounded substitutes establish policy/owner facts only, never SQL atomicity or broker acceptance.
internal sealed class DispositionUnitFixture : IAsyncDisposable
{
    internal readonly LocalApplicationIdentity Identity = LocalApplicationIdentity.Capture();
    internal readonly ScriptedLocalTransactionFactory Transactions = new();
    internal readonly DispositionUnitStore Store = new();
    internal readonly PublicationNumberCodec Codec = new();
    internal readonly MessagingSnapshot Snapshot;
    internal readonly MessagingResourceBudget Resources;
    internal readonly MessageSubscriptionDefinition<int> Subscription;
    internal readonly MessageDeliveryDispositionCoordinator Coordinator;
    internal readonly EncodedMessage Message;
    internal int Wakes;

    internal DispositionUnitFixture(MessagingOptions? options = null)
    {
        Subscription = new("Countries", "Ledger", "events", Codec,
            _ => throw new InvalidOperationException("No handler may run during bookkeeping."),
            (_, _, _, _) => ValueTask.FromResult(new CapabilityTarget(Identity.Application, Identity.Environment, "country", "MT")),
            ["country.read"], new() { MaximumDeliveryOrdinals = 3, RetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)], RetryJitterRatio = 0.1 });
        Snapshot = new(options ?? new(), [new MessageRouteDefinition<int>("events", MessageKind.Event, Codec)],
            [new("Messaging", "Ledger")], [Subscription]);
        Resources = new(Snapshot.Settings);
        Coordinator = new(Snapshot, Store, Transactions, Identity, () => Wakes++, () => 0.5);
        Message = MessageWire.Encode(new(Guid.NewGuid(), MessageKind.Event, Codec.Contract, "producer", "test", DateTimeOffset.UnixEpoch, "1"u8.ToArray()));
    }

    internal MessageFailedDelivery Failure(MessageDeliveryFailureKind kind = MessageDeliveryFailureKind.HandlerFault, int ordinal = 1)
    {
        var provenance = new MessageProvenance("Broker", "fixture", new("rabbit:Broker/fixture", "producer"), "producer", "producer", "test");
        var attempt = new MessageProcessingAttempt(new(Identity.Application, Identity.Environment, Subscription.Alias, Message.Envelope.MessageId),
            Message, provenance, ordinal, Purpose: ordinal == 1 ? MessageRecordPurpose.Application : MessageRecordPurpose.Retry,
            HandoffRecordId: ordinal == 1 ? null : Guid.NewGuid());
        return new(Message, Subscription, kind, "messaging.handler_failed", attempt, true,
            new(TransactionOutcome.NotCommitted, TransactionEndReason.RollbackRequested, false, true, true));
    }

    internal static TransactionCompletion Completion(TransactionOutcome outcome) => new(outcome, TransactionEndReason.CommitRequested,
        outcome != TransactionOutcome.NotCommitted, outcome != TransactionOutcome.Committed, outcome == TransactionOutcome.NotCommitted);

    internal void Script(params TransactionOutcome[] outcomes)
    {
        foreach (TransactionOutcome outcome in outcomes)
        {
            Transactions.Enqueue(Completion(outcome));
        }
    }

    public ValueTask DisposeAsync() => Resources.DisposeAsync();
}

internal sealed class DispositionUnitStore : IMessagingStore
{
    internal int Calls;
    internal int Rejections;
    internal MessageDispositionRequest? Disposition;
    internal MessageRejection? Rejection;
    internal ILocalTransactionContext? Context;
    internal MessageDispositionResultKind Winner = MessageDispositionResultKind.Secured;
    internal Exception? Fault;
    internal Action? OnRejection;
    internal MessageRedriveTransition? Redrive;
    internal MessageInspectionRequest? Inspection;
    internal MessageMaintenanceRequest? Maintenance;
    internal MessageProcessingRecord? Processing;
    internal MessageMaintenanceCounts Counts = new(1, 0, 0);
    internal Func<Task>? Hold;
    internal bool AttachResult = true;
    internal MessageReplyAttachment? Attachment;

    public Task<bool> AttachReplyAsync(ILocalTransactionContext context, MessageReplyAttachment attachment, CancellationToken cancellationToken = default)
    {
        Assert.AreSame(Context, context);
        Attachment = attachment;
        return Task.FromResult(AttachResult);
    }

    public Task<MessageDispositionResult> SecureDispositionAsync(ILocalTransactionContext context, MessageDispositionRequest request, CancellationToken cancellationToken = default)
    {
        Calls++;
        Context = context;
        Disposition = request;
        if (Fault is not null)
        {
            return Task.FromException<MessageDispositionResult>(Fault);
        }
        MessageProcessingDisposition state = Winner == MessageDispositionResultKind.AlreadyHandled ? MessageProcessingDisposition.Handled : request.Disposition;
        var record = new MessageProcessingRecord(request.Attempt.Key, request.Attempt.Message.Digest, request.Attempt.Provenance,
            request.Handoff.DeliveryOrdinal, state, 2, request.Attempt.Generation, DateTimeOffset.UnixEpoch, request.Handoff.RecordId);
        return Task.FromResult(new MessageDispositionResult(Winner, record, request.Handoff.RecordId));
    }

    public Task RecordRejectionAsync(ILocalTransactionContext context, MessageRejection rejection, CancellationToken cancellationToken = default)
    {
        Calls++;
        Rejections++;
        Context = context;
        Rejection = rejection;
        OnRejection?.Invoke();
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<MessageInspectionRecord>> InspectAsync(ILocalTransactionContext context, MessageInspectionRequest request, CancellationToken cancellationToken = default)
    {
        Calls++;
        Context = context;
        Inspection = request;
        if (Hold is not null)
        {
            await Hold();
        }
        return [];
    }

    public Task<MessageProcessingRecord?> ReadProcessingAsync(ILocalTransactionContext context, MessageProcessingKey key, CancellationToken cancellationToken = default)
        => Task.FromResult(Processing);

    public Task<MessageRedriveResult> RedriveAsync(ILocalTransactionContext context, MessageRedriveTransition transition, CancellationToken cancellationToken = default)
    {
        Calls++;
        Context = context;
        Redrive = transition;
        return Task.FromResult(new MessageRedriveResult(MessageRedriveDisposition.Prepared, Processing, Guid.NewGuid()));
    }

    public Task<MessageMaintenanceCounts> MaintainAsync(ILocalTransactionContext context, MessageMaintenanceRequest request, CancellationToken cancellationToken = default)
    {
        Calls++;
        Context = context;
        Maintenance = request;
        return Task.FromResult(Counts);
    }

    public Task<MessageStageReceipt> StageAsync(ILocalTransactionContext context, MessageOutgoingRecord record, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<MessageReservation>> ReserveAsync(ILocalTransactionContext context, MessageReservationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<EncodedMessage?> ReadReservedMessageAsync(ILocalTransactionContext context, MessageReservation reservation, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> RecordPublicationAsync(ILocalTransactionContext context, MessagePublicationUpdate update, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<MessageSubmissionObservation> ReadSubmissionAsync(ILocalTransactionContext context, MessageStageReceipt receipt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<MessageProcessingClaim> ClaimProcessingAsync(ILocalTransactionContext context, MessageProcessingAttempt attempt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> CompleteProcessingAsync(ILocalTransactionContext context, MessageProcessingCompletion completion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<MessageStoreObservation> ObserveAsync(ILocalTransactionContext context, MessageObservationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

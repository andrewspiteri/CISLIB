using System.Text.Json;
using CIS.CommonTesting;
using CIS.Messaging;
using CIS.Security;
using CIS.Transactions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Messaging;

[TestClass]
[DoNotParallelize]
public sealed class MessageOutboxTests
{
    [TestMethod]
    public async Task ExplicitStagingUsesTheOriginalContextAndNeverCompletesOrPublishesForItsCaller()
    {
        await using var fixture = new PublicationUnitFixture();
        fixture.Script(TransactionOutcome.Committed);
        await using ILocalTransaction owner = await fixture.Transactions.BeginAsync("Ledger");
        MessageStageResult result = await fixture.Outbox.SendAsync(owner.Context, "commands", 7);
        Assert.AreEqual(MessageStageDisposition.Staged, result.Disposition);
        Assert.AreSame(owner.Context, fixture.Store.StageContext);
        Assert.AreEqual(LocalTransactionStatus.Active, owner.Status);
        Assert.IsNull(owner.Completion);
        Assert.AreEqual(0, fixture.Broker.Calls);
        Assert.AreEqual(fixture.Store.Staged!.Message.Envelope.MessageId, result.Receipt!.MessageId);
        Assert.AreEqual(fixture.Store.Staged.Message.Digest, result.Receipt.Digest);
        Assert.AreEqual(0, fixture.Store.ReadCalls);
    }

    [TestMethod]
    [DataRow("codec")]
    [DataRow("store")]
    [DataRow("unreadable")]
    [DataRow("receipt")]
    public async Task StagingFaultsPreserveOriginalExceptionsAndPreventApparentlySuccessfulAtomicCommit(string boundary)
    {
        await using var fixture = new PublicationUnitFixture();
        fixture.Script(TransactionOutcome.Committed);
        await using ILocalTransaction owner = await fixture.Transactions.BeginAsync("Ledger");
        var original = new InvalidOperationException("application.programming_fault");
        if (boundary == "codec")
        {
            fixture.Codec.Fault = original;
        }
        else if (boundary == "store")
        {
            fixture.Store.StageFault = original;
        }
        else if (boundary == "unreadable")
        {
            fixture.Codec.Readable = false;
        }
        else
        {
            fixture.Store.WrongReceipt = true;
        }
        InvalidOperationException observed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => fixture.Outbox.SendAsync(owner.Context, "commands", 7));
        if (boundary is "codec" or "store")
        {
            Assert.AreSame(original, observed);
        }
        Assert.IsTrue(owner.Context.IsRollbackOnly);
        Assert.AreEqual(TransactionOutcome.NotCommitted, (await owner.CommitAsync()).Outcome);
        Assert.AreEqual(0, fixture.Broker.Calls);
    }

    [TestMethod]
    public async Task MemoryAndStorageRefusalsPoisonExplicitStagingAndRejectConvenienceWorkBeforeUnboundedBegin()
    {
        await using var fixture = new PublicationUnitFixture();
        fixture.Script(TransactionOutcome.Committed, TransactionOutcome.Committed, TransactionOutcome.Committed);
        await using ILocalTransaction owner = await fixture.Transactions.BeginAsync("Ledger");
        using IDisposable allBytes = await fixture.Resources.AcquireAsync(PublicationUnitFixture.ByteCapacity);
        MessageStageResult refused = await fixture.Outbox.SendAsync(owner.Context, "commands", 7);
        Assert.AreEqual(MessageStageDisposition.Overloaded, refused.Disposition);
        Assert.IsTrue(owner.Context.IsRollbackOnly);
        MessageSubmission notAccepted = await fixture.Client.SendAsync("commands", 7);
        Assert.AreEqual(MessageSubmissionOutcome.NotSubmitted, notAccepted.Outcome);
        Assert.IsNull(notAccepted.Receipt);
        Assert.AreEqual(1, fixture.Transactions.CreatedTransactions.Count);
        allBytes.Dispose();
        fixture.Store.StageFault = new MessageStorageCapacityException();
        await using ILocalTransaction quotaOwner = await fixture.Transactions.BeginAsync("Ledger");
        Assert.AreEqual(MessageStageDisposition.Overloaded,
            (await fixture.Outbox.SendAsync(quotaOwner.Context, "commands", 7)).Disposition);
        Assert.IsTrue(quotaOwner.Context.IsRollbackOnly);
        MessageSubmission quota = await fixture.Client.SendAsync("commands", 7);
        Assert.AreEqual(MessageSubmissionOutcome.NotSubmitted, quota.Outcome);
        Assert.AreEqual(TransactionOutcome.NotCommitted, quota.Completion!.Outcome);
        Assert.AreEqual("messaging.storage_capacity", quota.Error!.Code);
    }

    [TestMethod]
    [DataRow(TransactionOutcome.Committed)]
    [DataRow(TransactionOutcome.NotCommitted)]
    [DataRow(TransactionOutcome.Unknown)]
    public async Task ConvenienceSubmissionReturnsActualPersistenceAndStableIdsWithoutInventingBrokerSuccess(TransactionOutcome outcome)
    {
        await using var fixture = new PublicationUnitFixture();
        fixture.Script(outcome);
        MessageSubmission result = await fixture.Client.PublishAsync("events", 42);
        MessageSubmissionOutcome expected = outcome switch
        {
            TransactionOutcome.Committed => MessageSubmissionOutcome.OutboxCommitted,
            TransactionOutcome.NotCommitted => MessageSubmissionOutcome.NotSubmitted,
            _ => MessageSubmissionOutcome.Unknown
        };
        Assert.AreEqual(expected, result.Outcome);
        Assert.AreEqual(outcome, result.Completion!.Outcome);
        Assert.AreEqual(fixture.Store.Staged!.RecordId, result.Receipt!.RecordId);
        Assert.AreEqual(fixture.Store.Staged.Message.Envelope.MessageId, result.Receipt.MessageId);
        Assert.AreEqual(0, fixture.Broker.Calls);
        Assert.AreEqual(outcome == TransactionOutcome.Committed ? 1 : 0, fixture.Wakes);
        Assert.IsTrue(fixture.Transactions.CreatedTransactions.Single().DisposeRequested);
    }

    [TestMethod]
    public async Task StopClosesNewStagingAndJoinsTheAcceptedStoreCallWithoutTakingTheCallersTransaction()
    {
        await using var fixture = new PublicationUnitFixture();
        fixture.Script(TransactionOutcome.Committed, TransactionOutcome.Committed);
        await using ILocalTransaction first = await fixture.Transactions.BeginAsync("Ledger");
        await using ILocalTransaction second = await fixture.Transactions.BeginAsync("Ledger");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Store.StageHold = async () =>
        {
            entered.TrySetResult();
            await release.Task;
        };
        Task<MessageStageResult> accepted = fixture.Outbox.SendAsync(first.Context, "commands", 7);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task stopping = fixture.Outbox.StopAsync();
        try
        {
            Assert.IsFalse(stopping.IsCompleted);
            Assert.AreEqual(MessageStageDisposition.Unavailable,
                (await fixture.Outbox.SendAsync(second.Context, "commands", 8)).Disposition);
            Assert.AreEqual(LocalTransactionStatus.Active, first.Status);
        }
        finally
        {
            release.TrySetResult();
        }
        Assert.AreEqual(MessageStageDisposition.Staged, (await accepted).Disposition);
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(LocalTransactionStatus.Active, first.Status);
    }

    [TestMethod]
    public async Task UnselectedTypedRequestsReturnDefinedUnavailableWithoutSubmittingAnything()
    {
        await using var fixture = new PublicationUnitFixture();
        fixture.Script(TransactionOutcome.Committed);
        await using ILocalTransaction owner = await fixture.Transactions.BeginAsync("Ledger");
        var request = new MessageRequest<int>("lookup", 7);
        Assert.AreEqual(MessageStageDisposition.Unavailable,
            (await fixture.Outbox.StageRequestAsync<int, int>(owner.Context, request)).Disposition);
        MessageRequestResult<int> result = await fixture.Client.RequestAsync<int, int>(request);
        Assert.AreEqual(MessageExchangeOutcome.Unavailable, result.Outcome);
        Assert.AreEqual(MessageSubmissionOutcome.NotSubmitted, result.Submission.Outcome);
        Assert.AreEqual(0, fixture.Store.StageCalls);
        Assert.AreEqual(1, fixture.Transactions.CreatedTransactions.Count);
    }
}

// Ordinary bounded unit substitutes. They prove orchestration, never SQL or broker durability.
internal sealed class PublicationUnitFixture : IAsyncDisposable
{
    internal const int ByteCapacity = 1_000_000;
    internal readonly PublicationNumberCodec Codec = new();
    internal readonly PublicationStore Store = new();
    internal readonly PublicationBroker Broker = new();
    internal readonly ScriptedLocalTransactionFactory Transactions = new(32, 32);
    internal readonly ManualTimeProvider Clock = new(DateTimeOffset.UnixEpoch);
    internal readonly LocalApplicationIdentity Identity = LocalApplicationIdentity.Capture();
    internal readonly MessagingResourceBudget Resources;
    internal readonly MessageOutbox Outbox;
    internal readonly MessageSubmissionClient Client;
    internal readonly MessageOutboxPublisher Publisher;
    internal int Wakes;

    internal PublicationUnitFixture()
    {
        var snapshot = new MessagingSnapshot(new MessagingOptions
        {
            MaximumPayloadBytes = 16,
            MaximumEncodedBufferBytes = ByteCapacity,
            PublisherConcurrency = 2,
            MaximumConcurrentDeliveries = 2,
            MaximumOutstandingRequests = 4
        }, [new MessageRouteDefinition<int>("commands", MessageKind.Command, Codec),
            new MessageRouteDefinition<int>("events", MessageKind.Event, Codec)], [new("Messaging", "Ledger")]);
        Resources = new(snapshot.Settings);
        Outbox = new(snapshot, Store, Resources, Identity, Clock);
        Client = new(Outbox, Transactions, () => Wakes++);
        Publisher = new(snapshot, Store, Transactions, Broker, Resources, Identity, Clock);
        Store.Body = MessageWire.Encode(new(Guid.NewGuid(), MessageKind.Command, Codec.Contract,
            Identity.Application, Identity.Environment, DateTimeOffset.UnixEpoch, "7"u8.ToArray()));
        Store.Rows = [new(Guid.NewGuid(), Store.Body.Envelope.MessageId, Identity.Application, Identity.Environment,
            new("commands"), MessageRecordPurpose.Application, 1, 0, null, Store.Body.Digest, Store.Body.Length,
            Guid.NewGuid(), 1, DateTimeOffset.UnixEpoch.AddMinutes(1), DateTimeOffset.UnixEpoch, 1)];
    }

    internal void Script(params TransactionOutcome[] outcomes)
    {
        foreach (TransactionOutcome outcome in outcomes)
        {
            Transactions.Enqueue(new(outcome, TransactionEndReason.CommitRequested,
                outcome != TransactionOutcome.NotCommitted, outcome != TransactionOutcome.Committed,
                outcome == TransactionOutcome.NotCommitted));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Outbox.StopAsync();
        await Publisher.DisposeAsync();
        await Resources.DisposeAsync();
    }
}

internal sealed class PublicationNumberCodec : IMessageCodec<int>
{
    internal Exception? Fault;
    internal bool Readable = true;
    public MessageContract Contract { get; } = new("sample.number", 1);
    public byte[] Encode(int value)
    {
        if (Fault is not null)
        {
            throw Fault;
        }
        return JsonSerializer.SerializeToUtf8Bytes(value);
    }
    public bool TryDecode(MessageContract contract, ReadOnlyMemory<byte> json, out int value)
    {
        value = JsonSerializer.Deserialize<int>(json.Span);
        return Readable && contract == Contract;
    }
}

internal sealed class PublicationBroker : IMessagePublisher
{
    internal Func<MessagePublication, CancellationToken, Task<MessagePublicationResult>>? OnPublish;
    internal MessagePublication? Last;
    internal int Calls;
    public Task<MessagePublicationResult> PublishAsync(MessagePublication publication, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Calls);
        Last = publication;
        return OnPublish?.Invoke(publication, cancellationToken) ?? Task.FromResult(new MessagePublicationResult(MessagePublicationDisposition.Confirmed));
    }
}

internal sealed class PublicationStore : IMessagingStore
{
    internal MessageOutgoingRecord? Staged;
    internal ILocalTransactionContext? StageContext;
    internal Exception? StageFault;
    internal Func<Task>? StageHold;
    internal bool WrongReceipt;
    internal int StageCalls;
    internal IReadOnlyList<MessageReservation> Rows = [];
    internal EncodedMessage Body = null!;
    internal bool MissingBody;
    internal bool UpdateResult = true;
    internal int ReadCalls;
    internal MessagePublicationUpdate? LastUpdate;
    internal CancellationToken SettlementToken;

    public async Task<MessageStageReceipt> StageAsync(ILocalTransactionContext context, MessageOutgoingRecord record, CancellationToken cancellationToken = default)
    {
        StageCalls++;
        StageContext = context;
        Staged = record;
        if (StageFault is not null)
        {
            throw StageFault;
        }
        if (StageHold is not null)
        {
            await StageHold();
        }
        return new(WrongReceipt ? Guid.NewGuid() : record.RecordId, record.Message.Envelope.MessageId,
            context.DatabaseName, record.Application, record.Environment, record.Message.Digest);
    }
    public Task<IReadOnlyList<MessageReservation>> ReserveAsync(ILocalTransactionContext context, MessageReservationRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<MessageReservation>>(Rows.Select(row => row with { Owner = request.Owner }).ToArray());
    public Task<EncodedMessage?> ReadReservedMessageAsync(ILocalTransactionContext context, MessageReservation reservation, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref ReadCalls);
        return Task.FromResult(MissingBody ? null : Body);
    }
    public Task<bool> RecordPublicationAsync(ILocalTransactionContext context, MessagePublicationUpdate update, CancellationToken cancellationToken = default)
    {
        LastUpdate = update;
        SettlementToken = cancellationToken;
        return Task.FromResult(UpdateResult);
    }
    public Task<MessageSubmissionObservation> ReadSubmissionAsync(ILocalTransactionContext context, MessageStageReceipt receipt, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("No submission read is expected in this publication unit substitute.");
    public Task<MessageProcessingRecord?> ReadProcessingAsync(ILocalTransactionContext context, MessageProcessingKey key, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("No consumer work is expected in this publication unit substitute.");
    public Task<MessageProcessingClaim> ClaimProcessingAsync(ILocalTransactionContext context, MessageProcessingAttempt attempt, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("No consumer work is expected in this publication unit substitute.");
    public Task<bool> CompleteProcessingAsync(ILocalTransactionContext context, MessageProcessingCompletion completion, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("No consumer work is expected in this publication unit substitute.");
    public Task<MessageDispositionResult> SecureDispositionAsync(ILocalTransactionContext context, MessageDispositionRequest request, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("No consumer work is expected in this publication unit substitute.");
    public Task RecordRejectionAsync(ILocalTransactionContext context, MessageRejection rejection, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("No consumer work is expected in this publication unit substitute.");
    public Task<MessageRedriveResult> RedriveAsync(ILocalTransactionContext context, MessageRedriveTransition transition, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("No administration is expected in this publication unit substitute.");
    public Task<IReadOnlyList<MessageInspectionRecord>> InspectAsync(ILocalTransactionContext context, MessageInspectionRequest request, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("No administration is expected in this publication unit substitute.");
    public Task<MessageMaintenanceCounts> MaintainAsync(ILocalTransactionContext context, MessageMaintenanceRequest request, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("No administration is expected in this publication unit substitute.");
    public Task<MessageStoreObservation> ObserveAsync(ILocalTransactionContext context, MessageObservationRequest request, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("No observation is expected in this publication unit substitute.");
}

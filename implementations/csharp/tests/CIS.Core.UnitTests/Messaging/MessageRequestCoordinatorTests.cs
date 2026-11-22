using System.Text.Json;
using System.Runtime.InteropServices;
using CIS.CommonTesting;
using CIS.Messaging;
using CIS.Security;
using CIS.Security.Hosting;
using CIS.Tests.UnitTests.Security;
using CIS.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Messaging;

[TestClass]
[DoNotParallelize]
public sealed class MessageRequestCoordinatorTests
{
    [TestMethod]
    public async Task KnownSubmissionSettlesAndReleasesPreparationBeforeWakeAndTheRemainingReplyWait()
    {
        await using var fixture = new RequestUnitFixture(minimumBytes: true);
        fixture.Factory.Scripts.Enqueue();
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        bool preparationReleased = false;
        Task<MessageReplyHandling>? delivered = null;
        fixture.OnWake = () =>
        {
            preparationReleased = fixture.Resources.TryAcquireBytes(fixture.NormalCapacity, out IDisposable? lease);
            lease?.Dispose();
            delivered = ReceiveCheckedAsync();
        };
        async Task<MessageReplyHandling> ReceiveCheckedAsync()
        {
            try
            {
                MessageReplyHandling handling = await fixture.ReceiveAsync(fixture.Reply());
                if (handling != MessageReplyHandling.Acknowledge)
                {
                    throw new InvalidOperationException("The controlled reply was rejected: " + handling);
                }
                return handling;
            }
            catch
            {
                guard.Cancel();
                throw;
            }
        }
        MessageRequestResult<int> result = await fixture.Coordinator.RequestAsync<int, int>(new("Query", 7), guard.Token);
        Assert.IsNotNull(delivered, "Known committed submission must issue the publication wake hint.");
        Assert.AreEqual(MessageReplyHandling.Acknowledge, await delivered);
        Assert.AreEqual(MessageExchangeOutcome.ResponseReceived, result.Outcome);
        Assert.AreEqual(7, result.Response!.Decision!.Value);
        Assert.AreEqual(MessageSubmissionOutcome.OutboxCommitted, result.Submission.Outcome);
        Assert.AreEqual(TransactionOutcome.Committed, result.Submission.Completion!.Outcome);
        Assert.IsTrue(preparationReleased);
        Assert.IsTrue(fixture.Factory.Scripts.CreatedTransactions.Single().DisposeRequested);
    }

    [TestMethod]
    public async Task ProvisionalWaitReadsIndependentlyAndAnEarlyResponseRetainsItsBufferUntilConsumption()
    {
        await using var fixture = new RequestUnitFixture();
        fixture.Factory.Scripts.Enqueue();
        fixture.Factory.Scripts.Enqueue();
        fixture.Factory.Scripts.Enqueue();
        await using ILocalTransaction original = await fixture.Factory.BeginAsync("Ledger");
        MessageRequestStage<int> staged = await fixture.Coordinator.StageRequestAsync<int, int>(original.Context, new("Query", 7), CancellationToken.None);
        await using MessageRequestTicket<int> ticket = staged.Ticket!;
        fixture.Store.Observation = MessageSubmissionObservation.NotFound;
        MessageRequestResult<int> premature = await fixture.Coordinator.WaitForReplyAsync(ticket, CancellationToken.None);
        Assert.AreEqual(MessageExchangeOutcome.SubmissionNotConfirmed, premature.Outcome);
        Assert.AreEqual(LocalTransactionStatus.Active, original.Status);
        Assert.AreNotSame(original.Context, fixture.Store.ReadContext);
        Assert.IsFalse(fixture.Factory.Scripts.CreatedTransactions[0].CommitRequested);
        Assert.AreEqual(TransactionOutcome.Committed, (await original.CommitAsync()).Outcome);
        fixture.Store.Observation = MessageSubmissionObservation.Found;
        Assert.AreEqual(MessageReplyHandling.Acknowledge, await fixture.ReceiveAsync(fixture.Reply()));
        var actual = (MessageRpcTicket<int>)ticket;
        Assert.IsTrue(actual.HasRetainedResponse);
        Assert.IsFalse(fixture.Resources.TryAcquireBytes(fixture.NormalCapacity, out _));
        MessageRequestResult<int> completed = await fixture.Coordinator.WaitForReplyAsync(ticket, CancellationToken.None);
        Assert.AreEqual(MessageExchangeOutcome.ResponseReceived, completed.Outcome);
        Assert.AreEqual(MessageSubmissionOutcome.OutboxCommitted, completed.Submission.Outcome);
        Assert.IsNull(completed.Submission.Completion, "Independent observation cannot invent the original owner's completion.");
        Assert.IsFalse(actual.HasRetainedResponse);
        Assert.IsTrue(fixture.Resources.TryAcquireBytes(fixture.NormalCapacity, out IDisposable? all));
        all!.Dispose();
    }

    [TestMethod]
    [DataRow("none")]
    [DataRow("other")]
    public async Task UnrelatedStoreCancellationRemainsTheOriginalFaultDuringOverlappingCallerCancellation(string source)
    {
        await using var fixture = new RequestUnitFixture();
        fixture.Factory.Scripts.Enqueue();
        using var caller = new CancellationTokenSource();
        using var unrelated = new CancellationTokenSource();
        var original = new OperationCanceledException(source == "none" ? CancellationToken.None : unrelated.Token);
        fixture.Store.OnStage = (_, _, _) =>
        {
            caller.Cancel();
            return Task.FromException(original);
        };
        OperationCanceledException observed = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            fixture.Coordinator.RequestAsync<int, int>(new("Query", 7), caller.Token));
        Assert.AreSame(original, observed);
        Assert.AreEqual(TransactionOutcome.NotCommitted, fixture.Factory.Scripts.CreatedTransactions.Single().Completion!.Outcome);
        Assert.IsTrue(fixture.Resources.TryAcquireRequest(out IDisposable? replacement));
        replacement!.Dispose();
    }

    [TestMethod]
    [DataRow("factory")]
    [DataRow("codec")]
    public async Task UnrelatedAdmissionCancellationIsNotRewrittenWhenTheCallerCancels(string boundary)
    {
        await using var fixture = new RequestUnitFixture();
        using var caller = new CancellationTokenSource();
        var original = new OperationCanceledException(CancellationToken.None);
        if (boundary == "factory")
        {
            fixture.Factory.OnBegin = _ =>
            {
                caller.Cancel();
                return Task.FromException(original);
            };
        }
        else
        {
            fixture.Codec.OnEncode = _ =>
            {
                caller.Cancel();
                throw original;
            };
        }
        Assert.AreSame(original, await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            fixture.Coordinator.RequestAsync<int, int>(new("Query", 7), caller.Token)));
        Assert.AreEqual(0, fixture.Factory.Scripts.CreatedTransactions.Count);
    }

    [TestMethod]
    public async Task CallerCancellationWinnerSurvivesLaterDeadlineWhileActualOwnerCleanupIsHeld()
    {
        await using var fixture = new RequestUnitFixture();
        fixture.Factory.Scripts.Enqueue();
        using var caller = new CancellationTokenSource();
        var cleaning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Store.OnStage = (_, _, token) =>
        {
            caller.Cancel();
            return Task.FromException(new OperationCanceledException(token));
        };
        fixture.Factory.AfterDispose = async () =>
        {
            cleaning.TrySetResult();
            await release.Task;
        };
        Task<MessageRequestResult<int>> pending = fixture.Coordinator.RequestAsync<int, int>(new("Query", 7, timeout: TimeSpan.FromSeconds(1)), caller.Token);
        await cleaning.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task stopping = fixture.Coordinator.StopAsync();
        try
        {
            fixture.Clock.Advance(TimeSpan.FromSeconds(2));
            Assert.IsFalse(pending.IsCompleted);
            Assert.IsFalse(stopping.IsCompleted);
            Assert.IsFalse(fixture.Resources.TryAcquireRequest(out _));
        }
        finally
        {
            release.TrySetResult();
        }
        MessageRequestResult<int> result = await pending;
        Assert.AreEqual(MessageExchangeOutcome.Cancelled, result.Outcome);
        Assert.AreEqual(MessageSubmissionOutcome.NotSubmitted, result.Submission.Outcome);
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(fixture.Resources.TryAcquireRequest(out IDisposable? lease));
        lease!.Dispose();
    }

    [TestMethod]
    public async Task DeadlineCleanupFaultReleasesEveryPermitAndIsVisibleFromTheTicketAndCoordinatorStop()
    {
        var original = new IOException("controlled timer disposal fault after actual cleanup");
        await using var fixture = new RequestUnitFixture(timerFault: original);
        fixture.ExpectedCleanupFault = original;
        fixture.Factory.Scripts.Enqueue();
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Ledger");
        MessageRequestTicket<int> ticket = (await fixture.Coordinator.StageRequestAsync<int, int>(owner.Context, new("Query", 7), CancellationToken.None)).Ticket!;
        Assert.AreSame(original, await Assert.ThrowsExactlyAsync<IOException>(() => ticket.DisposeAsync().AsTask()));
        Assert.IsTrue(fixture.FaultClock!.DisposedActualTimer);
        Assert.IsTrue(fixture.Resources.TryAcquireRequest(out IDisposable? lease));
        lease!.Dispose();
        Assert.AreSame(original, await Assert.ThrowsExactlyAsync<IOException>(fixture.Coordinator.StopAsync));
    }

    [TestMethod]
    public async Task AdmissionCutoffDoesNotSynchronouslyBlockOnATimersDisposalPrologueButStopJoinsIt()
    {
        await using var fixture = new RequestUnitFixture(holdTimer: true);
        fixture.Factory.Scripts.Enqueue();
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Ledger");
        MessageRequestTicket<int> ticket = (await fixture.Coordinator.StageRequestAsync<int, int>(owner.Context,
            new("Query", 7), CancellationToken.None)).Ticket!;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        fixture.FaultClock!.OnDispose = () =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("The test must release its owned timer cleanup.");
            }
        };
        Task disposing = ticket.DisposeAsync().AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task stopping = fixture.Coordinator.StopAsync();
            Assert.IsFalse(stopping.IsCompleted);
            Assert.IsFalse(disposing.IsCompleted);
            Assert.IsFalse(fixture.Resources.TryAcquireRequest(out _));
        }
        finally
        {
            release.Set();
        }
        await disposing.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(fixture.FaultClock.DisposedActualTimer);
    }

    [TestMethod]
    public async Task DisposedUnusedTicketClearsABorrowedCodecInputWhileThePublicReferenceRemainsUsable()
    {
        await using var fixture = new RequestUnitFixture();
        fixture.Factory.Scripts.Enqueue();
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Ledger");
        MessageRequestTicket<ReadOnlyMemory<byte>> ticket = (await fixture.Coordinator.StageRequestAsync<int, ReadOnlyMemory<byte>>(
            owner.Context, new("Bytes", 7), CancellationToken.None)).Ticket!;
        EncodedMessage request = fixture.Store.Staged.Single().Message;
        byte[] payload = MessageReplyProtocol.Encode(fixture.BytesDefinition, request.Digest, fixture.BorrowedCodec,
            new(MessageProtocolDisposition.Completed, CIS.Operations.OperationDecision<ReadOnlyMemory<byte>>.Succeed("7"u8.ToArray())),
            fixture.Snapshot.Settings.Limits);
        EncodedMessage reply = MessageWire.Encode(new(Guid.NewGuid(), MessageKind.Reply, fixture.BorrowedCodec.Contract,
            "caller-a", "Development", SecurityTestSupport.Now, payload, request.Envelope.RequestAttemptId,
            inReplyToMessageId: request.Envelope.MessageId));
        Assert.AreEqual(MessageReplyHandling.Acknowledge, await fixture.ReceiveAsync(reply));
        WeakReference<byte[]> input = fixture.BorrowedCodec.LastInput!;
        Assert.IsTrue(((MessageRpcTicket<ReadOnlyMemory<byte>>)ticket).HasRetainedResponse);
        Assert.IsFalse(fixture.Resources.TryAcquireBytes(fixture.NormalCapacity - reply.Length, out _),
            "Both retained wire and the codec's legal input slice need owned encoded admission.");
        await ticket.DisposeAsync();
        Assert.IsTrue(fixture.Resources.TryAcquireBytes(fixture.NormalCapacity, out IDisposable? all));
        all!.Dispose();
        // Actual disposal/permit settlement above proves cleanup; GC only checks payload reachability afterwards.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.IsFalse(input.TryGetTarget(out _), "An unused disposed ticket must not retain its private decoded value.");
        Assert.AreEqual(request.Envelope.MessageId, ticket.Reference.MessageId);
        GC.KeepAlive(ticket);
    }

    [TestMethod]
    [DataRow("none")]
    [DataRow("other")]
    public async Task IndependentReceiptReadPreservesAnUnrelatedCancellationDuringCallerCancellation(string source)
    {
        await using var fixture = new RequestUnitFixture();
        fixture.Factory.Scripts.Enqueue();
        fixture.Factory.Scripts.Enqueue();
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Ledger");
        await using MessageRequestTicket<int> ticket = (await fixture.Coordinator.StageRequestAsync<int, int>(owner.Context,
            new("Query", 7), CancellationToken.None)).Ticket!;
        using var caller = new CancellationTokenSource();
        using var other = new CancellationTokenSource();
        var original = new OperationCanceledException(source == "none" ? CancellationToken.None : other.Token);
        fixture.Store.OnRead = _ =>
        {
            caller.Cancel();
            return Task.FromException(original);
        };
        Assert.AreSame(original, await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            fixture.Coordinator.WaitForReplyAsync(ticket, caller.Token)));
        Assert.IsTrue(fixture.Factory.Scripts.CreatedTransactions[1].DisposeRequested);
    }

    [TestMethod]
    public async Task ChangedRequestDigestCannotCompleteTheLegitimateWaiter()
    {
        await using var fixture = new RequestUnitFixture();
        fixture.Factory.Scripts.Enqueue();
        fixture.Factory.Scripts.Enqueue();
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Ledger");
        await using MessageRequestTicket<int> ticket = (await fixture.Coordinator.StageRequestAsync<int, int>(owner.Context,
            new("Query", 7), CancellationToken.None)).Ticket!;
        await owner.CommitAsync();
        EncodedMessage request = fixture.Store.Staged.Single().Message;
        var changed = MessageWire.Encode(new(request.Envelope.MessageId, MessageKind.Request, request.Envelope.Contract,
            request.Envelope.ProducerApplication, request.Envelope.ProducerEnvironment, request.Envelope.CreatedAtUtc, "8"u8.ToArray(),
            request.Envelope.RequestAttemptId, request.Envelope.ReplyRoute));
        Assert.AreEqual(MessageReplyHandling.Quarantine, await fixture.ReceiveAsync(fixture.Reply(changed, MessageProtocolDisposition.Denied)));
        Assert.IsFalse(((MessageRpcTicket<int>)ticket).HasRetainedResponse);
        Task<MessageRequestResult<int>> waiting = fixture.Coordinator.WaitForReplyAsync(ticket, CancellationToken.None);
        Assert.IsFalse(waiting.IsCompleted);
        Assert.AreEqual(MessageReplyHandling.Acknowledge, await fixture.ReceiveAsync(fixture.Reply()));
        Assert.AreEqual(MessageExchangeOutcome.ResponseReceived, (await waiting).Outcome);
    }

    [TestMethod]
    [DataRow(TransactionOutcome.NotCommitted)]
    [DataRow(TransactionOutcome.Unknown)]
    public async Task SubmissionEvidenceIsNotInferredFromAnUnresolvedOrRolledBackOwner(TransactionOutcome outcome)
    {
        await using var fixture = new RequestUnitFixture();
        fixture.Factory.Scripts.Enqueue(new(outcome, TransactionEndReason.CommitRequested,
            outcome != TransactionOutcome.NotCommitted, true, outcome == TransactionOutcome.NotCommitted));
        MessageRequestResult<int> result = await fixture.Coordinator.RequestAsync<int, int>(new("Query", 7), CancellationToken.None);
        Assert.AreEqual(outcome == TransactionOutcome.Unknown ? MessageExchangeOutcome.SubmissionUnknown : MessageExchangeOutcome.NotSubmitted, result.Outcome);
        Assert.AreEqual(outcome, result.Submission.Completion!.Outcome);
        Assert.IsNotNull(result.Reference);
        Assert.AreEqual(0, fixture.Wakes);
    }
}

// Portable owner scripts and a controlled native projector; no SQL or real broker claim is made here.
internal sealed class RequestUnitFixture : IAsyncDisposable
{
    internal readonly LocalApplicationIdentity Local = LocalApplicationIdentity.Capture();
    internal readonly ManualTimeProvider Clock = new(SecurityTestSupport.Now);
    internal readonly RequestNumberCodec Codec = new();
    internal readonly RequestStore Store = new();
    internal readonly RequestFactory Factory = new();
    internal readonly MessagingSnapshot Snapshot;
    internal readonly MessagingResourceBudget Resources;
    internal readonly MessageRequestCoordinator Coordinator;
    internal readonly MessageOutbox Outbox;
    internal readonly MessageRequestDefinition<int, int> Definition;
    internal readonly MessageRequestDefinition<int, ReadOnlyMemory<byte>> BytesDefinition;
    internal readonly BorrowedResponseCodec BorrowedCodec = new();
    internal readonly ServiceProvider Provider;
    internal readonly RequestTimerFaultClock? FaultClock;
    internal readonly int NormalCapacity;
    internal Action? OnWake;
    internal Exception? ExpectedCleanupFault;
    internal int Wakes;

    internal RequestUnitFixture(bool minimumBytes = false, Exception? timerFault = null, bool holdTimer = false)
    {
        var options = new MessagingOptions { MaximumPayloadBytes = 1024, MaximumConcurrentDeliveries = 2,
            MaximumOutstandingRequests = 1, PublisherConcurrency = 1, MaximumEncodedBufferBytes = 1_000_000 };
        if (minimumBytes)
        {
            options.MaximumEncodedBufferBytes = 12 * new MessagingLimits(1024).MaximumEnvelopeBytes;
        }
        Definition = new("Query", "commands", Codec, Codec, MessageRequestMode.Query, "caller-a", "Development");
        BytesDefinition = new("Bytes", "commands", Codec, BorrowedCodec, MessageRequestMode.Query, "caller-a", "Development");
        Snapshot = new(options, [new MessageRouteDefinition<int>("commands", MessageKind.Command, Codec)], [new("Messaging", "Ledger")], requests: [Definition, BytesDefinition]);
        Resources = new(Snapshot.Settings, true);
        NormalCapacity = checked((int)options.MaximumEncodedBufferBytes - 4 * Snapshot.Settings.Limits.MaximumEnvelopeBytes);
        FaultClock = timerFault is null && !holdTimer ? null : new(Clock, timerFault);
        TimeProvider time = FaultClock is null ? Clock : FaultClock;
        var transport = new RequestTransport(new(Local.Application, Local.Environment, Guid.NewGuid()));
        Outbox = new(Snapshot, Store, Resources, Local, time);
        Coordinator = new(Snapshot, Store, Factory, transport, Outbox, Resources, Local, time, wake: () =>
        {
            Wakes++;
            OnWake?.Invoke();
        });
        Outbox.BindRequests(Coordinator);
        var security = SecurityTestSupport.Options();
        security.Capabilities.Add("messaging.reply");
        security.Grants.Add(new(SecurityTestSupport.Profile, "messaging.reply", Local.Application, Local.Environment,
            subject: SecurityTestSupport.Subject, resourceSelection: CapabilityResourceSelection.ExactResource,
            resourceType: "messaging.reply", resourceIdentifier: "Query"));
        security.Grants.Add(new(SecurityTestSupport.Profile, "messaging.reply", Local.Application, Local.Environment,
            subject: SecurityTestSupport.Subject, resourceSelection: CapabilityResourceSelection.ExactResource,
            resourceType: "messaging.reply", resourceIdentifier: "Bytes"));
        Provider = SecurityTestSupport.Services(security).BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    internal EncodedMessage Reply(EncodedMessage? actual = null, MessageProtocolDisposition protocol = MessageProtocolDisposition.Completed)
    {
        EncodedMessage request = Store.Staged.First().Message;
        byte[] payload = MessageReplyProtocol.Encode(Definition, (actual ?? request).Digest, Codec,
            protocol == MessageProtocolDisposition.Completed ? new(protocol, CIS.Operations.OperationDecision<int>.Succeed(7)) : new(protocol), Snapshot.Settings.Limits);
        return MessageWire.Encode(new(Guid.NewGuid(), MessageKind.Reply, Codec.Contract, "caller-a", "Development", SecurityTestSupport.Now,
            payload, request.Envelope.RequestAttemptId, inReplyToMessageId: request.Envelope.MessageId));
    }

    internal async Task<MessageReplyHandling> ReceiveAsync(EncodedMessage reply)
    {
        await using SecurityHostContextScope scope = await SecurityHostContextScope.CreateAsync(Provider,
            SecurityTestSupport.Profile, SecurityTestSupport.NativeAuthentication());
        return await Coordinator.ReceiveReplyAsync(reply, scope.Services, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Coordinator.DisposeAsync();
        }
        catch (Exception error) when (ReferenceEquals(error, ExpectedCleanupFault))
        {
            // This case explicitly observed the same completed-cleanup fault from ticket and owner above.
        }
        await Outbox.StopAsync();
        await Resources.DisposeAsync();
        await Provider.DisposeAsync();
    }
}

internal sealed class RequestNumberCodec : IMessageCodec<int>
{
    internal Func<int, byte[]>? OnEncode;
    public MessageContract Contract { get; } = new("fixture.number", 1);
    public byte[] Encode(int value) => OnEncode?.Invoke(value) ?? JsonSerializer.SerializeToUtf8Bytes(value);
    public bool TryDecode(MessageContract contract, ReadOnlyMemory<byte> json, out int value)
    {
        value = JsonSerializer.Deserialize<int>(json.Span);
        return contract == Contract;
    }
}

internal sealed class RequestFactory : ILocalTransactionFactory
{
    internal readonly ScriptedLocalTransactionFactory Scripts = new();
    internal Func<CancellationToken, Task>? OnBegin;
    internal Func<Task>? AfterDispose;
    public Task<ILocalTransaction> BeginAsync(string databaseName, CancellationToken cancellationToken = default)
        => BeginAsync(databaseName, new(), cancellationToken);
    public async Task<ILocalTransaction> BeginAsync(string databaseName, LocalTransactionOptions options, CancellationToken cancellationToken = default)
    {
        if (OnBegin is { } before)
        {
            await before(cancellationToken);
        }
        return new RequestOwner(await Scripts.BeginAsync(databaseName, options, cancellationToken), this);
    }
    private sealed class RequestOwner(ILocalTransaction inner, RequestFactory owner) : ILocalTransaction
    {
        public ILocalTransactionContext Context => inner.Context;
        public LocalTransactionStatus Status => inner.Status;
        public TransactionCompletion? Completion => inner.Completion;
        public Task<TransactionCompletion> CommitAsync(CancellationToken cancellationToken = default) => inner.CommitAsync(cancellationToken);
        public Task<TransactionCompletion> RollbackAsync() => inner.RollbackAsync();
        public async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            if (owner.AfterDispose is { } after)
            {
                await after();
            }
        }
    }
}

internal sealed class RequestTransport(MessageReplyRoute endpoint) : IMessageTransport
{
    public MessageReplyRoute? ReplyEndpoint => endpoint;
    public bool IsReady => true;
    public Task StartAsync(Func<IMessageDelivery, CancellationToken, Task> dispatch, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopConsumersAsync() => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public Task<MessagePublicationResult> PublishAsync(MessagePublication publication, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("No broker publication belongs to this portable unit fixture.");
}

internal sealed class RequestStore : IMessagingStore
{
    internal readonly List<MessageOutgoingRecord> Staged = [];
    internal Func<ILocalTransactionContext, MessageOutgoingRecord, CancellationToken, Task>? OnStage;
    internal MessageSubmissionObservation Observation = MessageSubmissionObservation.Found;
    internal ILocalTransactionContext? ReadContext;
    internal Func<CancellationToken, Task>? OnRead;
    public async Task<MessageStageReceipt> StageAsync(ILocalTransactionContext context, MessageOutgoingRecord record, CancellationToken cancellationToken = default)
    {
        if (Staged.Count >= 16)
        {
            throw new InvalidOperationException("The unit fixture's fixed recording capacity is exhausted.");
        }
        Staged.Add(record);
        if (OnStage is { } stage)
        {
            await stage(context, record, cancellationToken);
        }
        return new(record.RecordId, record.Message.Envelope.MessageId, context.DatabaseName, record.Application, record.Environment, record.Message.Digest);
    }
    public async Task<MessageSubmissionObservation> ReadSubmissionAsync(ILocalTransactionContext context, MessageStageReceipt receipt, CancellationToken cancellationToken = default)
    {
        ReadContext = context;
        if (OnRead is { } read)
        {
            await read(cancellationToken);
        }
        return Observation;
    }
    public Task<IReadOnlyList<MessageReservation>> ReserveAsync(ILocalTransactionContext context, MessageReservationRequest request, CancellationToken cancellationToken = default) => Unexpected<IReadOnlyList<MessageReservation>>();
    public Task<EncodedMessage?> ReadReservedMessageAsync(ILocalTransactionContext context, MessageReservation reservation, CancellationToken cancellationToken = default) => Unexpected<EncodedMessage?>();
    public Task<bool> RecordPublicationAsync(ILocalTransactionContext context, MessagePublicationUpdate update, CancellationToken cancellationToken = default) => Unexpected<bool>();
    public Task<MessageProcessingRecord?> ReadProcessingAsync(ILocalTransactionContext context, MessageProcessingKey key, CancellationToken cancellationToken = default) => Unexpected<MessageProcessingRecord?>();
    public Task<MessageProcessingClaim> ClaimProcessingAsync(ILocalTransactionContext context, MessageProcessingAttempt attempt, CancellationToken cancellationToken = default) => Unexpected<MessageProcessingClaim>();
    public Task<bool> CompleteProcessingAsync(ILocalTransactionContext context, MessageProcessingCompletion completion, CancellationToken cancellationToken = default) => Unexpected<bool>();
    public Task<bool> AttachReplyAsync(ILocalTransactionContext context, MessageReplyAttachment attachment, CancellationToken cancellationToken = default) => Unexpected<bool>();
    public Task<MessageDispositionResult> SecureDispositionAsync(ILocalTransactionContext context, MessageDispositionRequest request, CancellationToken cancellationToken = default) => Unexpected<MessageDispositionResult>();
    public Task RecordRejectionAsync(ILocalTransactionContext context, MessageRejection rejection, CancellationToken cancellationToken = default) => Unexpected<bool>();
    public Task<MessageRedriveResult> RedriveAsync(ILocalTransactionContext context, MessageRedriveTransition transition, CancellationToken cancellationToken = default) => Unexpected<MessageRedriveResult>();
    public Task<IReadOnlyList<MessageInspectionRecord>> InspectAsync(ILocalTransactionContext context, MessageInspectionRequest request, CancellationToken cancellationToken = default) => Unexpected<IReadOnlyList<MessageInspectionRecord>>();
    public Task<MessageMaintenanceCounts> MaintainAsync(ILocalTransactionContext context, MessageMaintenanceRequest request, CancellationToken cancellationToken = default) => Unexpected<MessageMaintenanceCounts>();
    public Task<MessageStoreObservation> ObserveAsync(ILocalTransactionContext context, MessageObservationRequest request, CancellationToken cancellationToken = default) => Unexpected<MessageStoreObservation>();
    private static Task<T> Unexpected<T>() => Task.FromException<T>(new InvalidOperationException("No consumer/administration operation belongs to this unit fixture."));
}

internal sealed class BorrowedResponseCodec : IMessageCodec<ReadOnlyMemory<byte>>
{
    internal WeakReference<byte[]>? LastInput;
    public MessageContract Contract { get; } = new("fixture.number", 1);
    public byte[] Encode(ReadOnlyMemory<byte> value) => value.ToArray();
    public bool TryDecode(MessageContract contract, ReadOnlyMemory<byte> json, out ReadOnlyMemory<byte> value)
    {
        value = json;
        if (MemoryMarshal.TryGetArray(json, out ArraySegment<byte> segment))
        {
            LastInput = new(segment.Array!);
        }
        return contract == Contract;
    }
}

internal sealed class RequestTimerFaultClock(ManualTimeProvider inner, Exception? fault) : TimeProvider
{
    internal bool DisposedActualTimer;
    internal Action? OnDispose;
    public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();
    public override long GetTimestamp() => inner.GetTimestamp();
    public override long TimestampFrequency => inner.TimestampFrequency;
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        => new FaultTimer(inner.CreateTimer(callback, state, dueTime, period), this, fault);
    private sealed class FaultTimer(ITimer inner, RequestTimerFaultClock owner, Exception? fault) : ITimer
    {
        private int disposed;
        public bool Change(TimeSpan dueTime, TimeSpan period) => inner.Change(dueTime, period);
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                inner.Dispose();
                owner.OnDispose?.Invoke();
                owner.DisposedActualTimer = true;
                if (fault is not null)
                {
                    throw fault;
                }
            }
        }
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                await inner.DisposeAsync();
                owner.OnDispose?.Invoke();
                owner.DisposedActualTimer = true;
                if (fault is not null)
                {
                    throw fault;
                }
            }
        }
    }
}

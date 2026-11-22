using System.Buffers;
using System.Diagnostics.Metrics;
using CIS.CommonTesting;
using CIS.Messaging;
using CIS.Metrics;
using CIS.Security;
using CIS.Security.Hosting;
using CIS.Tests.UnitTests.Security;
using CIS.Tests.UnitTests.Metrics;
using CIS.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Messaging;

[TestClass]
[DoNotParallelize]
public sealed class MessageDispatcherTests
{
    [TestMethod]
    public async Task ActualIndependentSecurityScopeAndOriginalParticipantSettleBeforeAcknowledgement()
    {
        await using var fixture = new DispatchUnitFixture();
        fixture.Transactions.Enqueue();
        MessageHandlerContext? observed = null;
        fixture.OnHandle = (context, value, _) =>
        {
            observed = context;
            Assert.AreEqual(7, value);
            Assert.AreEqual("caller-a", context.Services.GetRequiredService<ICallerContextAccessor>().Current!.Application);
            Assert.IsFalse(fixture.Scopes.Single().Disposed);
            return Task.CompletedTask;
        };
        using var body = new ObservedMessageMemory(fixture.Message().CopyBytes());
        var delivery = fixture.Delivery(body.Memory);
        delivery.OnAcknowledge = () =>
        {
            Assert.IsTrue(fixture.Scopes.Single().Disposed);
            Assert.IsTrue(fixture.Transactions.CreatedTransactions.Single().DisposeRequested);
        };
        MessageDispatchResult result = await fixture.Dispatcher.DispatchAsync(delivery);
        Assert.AreEqual(MessageDispatchOutcome.Handled, result.Outcome);
        Assert.AreEqual(TransactionOutcome.Committed, result.Completion!.Outcome);
        Assert.AreSame(observed!.Transaction, fixture.Store.ClaimContext);
        Assert.AreSame(observed.Transaction, fixture.Store.CompleteContext);
        Assert.AreEqual(1, delivery.Acknowledgements);
        Assert.AreEqual(0, fixture.Dispositions.Calls);
    }

    [TestMethod]
    public async Task DuplicateStillAuthorizesButDoesNotConstructOrInvokeAnotherHandler()
    {
        await using var fixture = new DispatchUnitFixture();
        fixture.Transactions.Enqueue();
        fixture.Store.ClaimDisposition = MessageClaimDisposition.HandledDuplicate;
        var delivery = fixture.Delivery(fixture.Message().CopyBytes());
        MessageDispatchResult result = await fixture.Dispatcher.DispatchAsync(delivery);
        Assert.AreEqual(MessageDispatchOutcome.Duplicate, result.Outcome);
        Assert.AreEqual(1, fixture.Audit.Decisions.Count);
        Assert.AreEqual(0, fixture.HandlerCalls);
        Assert.IsNull(fixture.Store.CompleteContext);
        Assert.AreEqual(1, delivery.Acknowledgements);
    }

    [TestMethod]
    public async Task NormalKnownCommittedDuplicateEmitsOneDispositionCounterWithoutFailureBookkeeping()
    {
        await using var fixture = new DispatchUnitFixture(observe: true);
        fixture.Transactions.Enqueue();
        fixture.Store.ClaimDisposition = MessageClaimDisposition.HandledDuplicate;
        using var meter = new Meter("Messaging.DispatchDuplicate");
        await using var metrics = new MetricsRuntime(meter);
        using var listener = new MetricsTestListener(meter);
        fixture.Observations!.AttachMetrics(metrics);
        Assert.AreEqual(MessageDispatchOutcome.Duplicate,
            (await fixture.Dispatcher.DispatchAsync(fixture.Delivery(fixture.Message().CopyBytes()))).Outcome);
        MetricsPoint point = listener.Points("cis.messaging.dispositions").Single();
        Assert.AreEqual(1L, (long)point.Value);
        Assert.IsTrue(point.Tags.Any(tag => tag.Key == "disposition" && Equals(tag.Value, "duplicate")));
        Assert.AreEqual(0, fixture.Dispositions.Calls);
    }

    [TestMethod]
    public async Task OriginalUnknownIsNeitherAcknowledgedNorConvertedIntoAnotherScheduledOrdinal()
    {
        await using var fixture = new DispatchUnitFixture();
        fixture.Transactions.Enqueue(new(TransactionOutcome.Unknown, TransactionEndReason.CommitRequested, true, true, false));
        var delivery = fixture.Delivery(fixture.Message().CopyBytes());
        MessageDispatchResult result = await fixture.Dispatcher.DispatchAsync(delivery);
        Assert.AreEqual(MessageDispatchOutcome.Unknown, result.Outcome);
        Assert.AreEqual(MessageDeliveryFailureKind.PersistenceUnknown, fixture.Dispositions.Last!.Kind);
        Assert.AreEqual(TransactionOutcome.Unknown, fixture.Dispositions.Last.OriginalCompletion!.Outcome);
        Assert.AreEqual(0, delivery.Acknowledgements);
        Assert.AreEqual(1, delivery.Stops);
        Assert.AreEqual(1, fixture.Transactions.CreatedTransactions.Count);
    }

    [TestMethod]
    public async Task FailedHandlerRollsBackAndPassesOriginalFaultBeforeSecuredResponsibilityCanAcknowledge()
    {
        await using var fixture = new DispatchUnitFixture();
        fixture.Transactions.Enqueue();
        var original = new InvalidOperationException("private application details");
        fixture.OnHandle = (_, _, _) => Task.FromException(original);
        var delivery = fixture.Delivery(fixture.Message().CopyBytes());
        MessageDispatchResult result = await fixture.Dispatcher.DispatchAsync(delivery);
        MessageFailedDelivery failed = fixture.Dispositions.Last!;
        Assert.AreEqual(MessageDispatchOutcome.Retry, result.Outcome);
        Assert.AreEqual(MessageDeliveryFailureKind.HandlerFault, failed.Kind);
        Assert.IsTrue(failed.CanonicalEligible);
        Assert.AreSame(original, failed.OriginalFault!.SourceException);
        Assert.AreEqual(TransactionOutcome.NotCommitted, failed.OriginalCompletion!.Outcome);
        Assert.IsTrue(fixture.Transactions.CreatedTransactions.Single().DisposeRequested);
        Assert.IsNull(fixture.Store.CompleteContext);
        Assert.AreEqual(1, delivery.Acknowledgements);
    }

    [TestMethod]
    public async Task NativeAcknowledgementFaultRemainsTheOriginalFaultAfterKnownBusinessCommit()
    {
        await using var fixture = new DispatchUnitFixture();
        fixture.Transactions.Enqueue();
        var original = new IOException("controlled native connection generation loss");
        var delivery = fixture.Delivery(fixture.Message().CopyBytes());
        delivery.OnAcknowledge = () => throw original;
        IOException observed = await Assert.ThrowsExactlyAsync<IOException>(() => fixture.Dispatcher.DispatchAsync(delivery));
        Assert.AreSame(original, observed);
        Assert.AreEqual(TransactionOutcome.Committed, fixture.Transactions.CreatedTransactions.Single().Completion!.Outcome);
        Assert.AreEqual(0, fixture.Dispositions.Calls);
        Assert.AreEqual(1, fixture.HandlerCalls);
        Assert.IsTrue(fixture.Scopes.Single().Disposed);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DenialAndAuditFaultCannotClaimTheCanonicalIdentityOrInvokeApplicationEffects(bool auditFault)
    {
        await using var fixture = new DispatchUnitFixture(grant: auditFault);
        var original = new InvalidOperationException("controlled audit fault");
        if (auditFault)
        {
            fixture.Audit.Failure = original;
        }
        var delivery = fixture.Delivery(fixture.Message().CopyBytes());
        if (auditFault)
        {
            Assert.AreSame(original, await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Dispatcher.DispatchAsync(delivery)));
            Assert.AreEqual(0, fixture.Dispositions.Calls);
            Assert.AreEqual(0, delivery.Acknowledgements);
        }
        else
        {
            await fixture.Dispatcher.DispatchAsync(delivery);
            Assert.AreEqual(MessageDeliveryFailureKind.Rejected, fixture.Dispositions.Last!.Kind);
            Assert.IsFalse(fixture.Dispositions.Last.CanonicalEligible);
            Assert.IsNull(fixture.Dispositions.Last.OriginalCompletion);
        }
        Assert.AreEqual(0, fixture.Store.Claims);
        Assert.AreEqual(0, fixture.HandlerCalls);
        Assert.AreEqual(0, fixture.Transactions.CreatedTransactions.Count);
        Assert.IsTrue(fixture.Scopes.Single().Disposed);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OrdinarySharedSaturationWaitsBeforeCopyAndResumesWithoutStoppingTheConsumer(bool bytes)
    {
        await using var fixture = new DispatchUnitFixture();
        await using var peer = new MessagingResourceBudget(fixture.Snapshot.Settings);
        fixture.Transactions.Enqueue();
        using IDisposable held = bytes ? await peer.AcquireAsync(DispatchUnitFixture.ByteCapacity) : await peer.AcquireDeliveryAsync();
        using var body = new ObservedMessageMemory(fixture.Message().CopyBytes());
        var delivery = fixture.Delivery(body.Memory);
        Task<MessageDispatchResult> dispatch = fixture.Dispatcher.DispatchAsync(delivery);
        Assert.IsFalse(dispatch.IsCompleted);
        Assert.AreEqual(0, body.SpanReads, "Native borrowed bytes must not be copied or decoded before admission.");
        Assert.AreEqual(0, delivery.Stops);
        held.Dispose();
        Assert.AreEqual(MessageDispatchOutcome.Handled, (await dispatch.WaitAsync(TimeSpan.FromSeconds(5))).Outcome);
        Assert.IsGreaterThan(0, body.SpanReads);
        Assert.AreEqual(0, delivery.Stops);
    }

    [TestMethod]
    public async Task StopCancelsAdmissionWaitAndJoinsTheIndependentNativeRequeueBeforeReturning()
    {
        await using var fixture = new DispatchUnitFixture();
        await using var peer = new MessagingResourceBudget(fixture.Snapshot.Settings);
        using IDisposable held = await peer.AcquireDeliveryAsync();
        var requeueEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRequeue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delivery = fixture.Delivery(fixture.Message().CopyBytes());
        delivery.OnRequeue = async token =>
        {
            Assert.IsFalse(token.IsCancellationRequested);
            requeueEntered.TrySetResult();
            await releaseRequeue.Task;
        };
        Task<MessageDispatchResult> dispatch = fixture.Dispatcher.DispatchAsync(delivery);
        Task stop = fixture.Dispatcher.StopAsync();
        try
        {
            await requeueEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(stop.IsCompleted);
        }
        finally
        {
            releaseRequeue.TrySetResult();
        }
        Assert.AreEqual(MessageDispatchOutcome.Cancelled, (await dispatch).Outcome);
        await stop.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, delivery.Requeues);
        Assert.AreEqual(0, fixture.Store.Claims);
        Assert.AreEqual(0, delivery.Acknowledgements);
    }

    [TestMethod]
    public async Task StopKeepsAnAdmittedHandlersScopeAliveUntilTheHandlerAndOriginalRollbackActuallySettle()
    {
        await using var fixture = new DispatchUnitFixture();
        fixture.Transactions.Enqueue();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.OnHandle = async (context, _, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            Assert.IsFalse(fixture.Scopes.Single().Disposed);
            Assert.AreEqual("caller-a", context.Services.GetRequiredService<ICallerContextAccessor>().Current!.Application);
        };
        var delivery = fixture.Delivery(fixture.Message().CopyBytes());
        delivery.OnRequeue = _ =>
        {
            Assert.IsTrue(fixture.Scopes.Single().Disposed);
            Assert.IsTrue(fixture.Transactions.CreatedTransactions.Single().DisposeRequested);
            Assert.AreEqual(TransactionOutcome.NotCommitted, fixture.Transactions.CreatedTransactions.Single().Completion!.Outcome);
            return Task.CompletedTask;
        };
        Task<MessageDispatchResult> dispatch = fixture.Dispatcher.DispatchAsync(delivery);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task stop = fixture.Dispatcher.StopAsync();
        try
        {
            Assert.IsFalse(stop.IsCompleted);
            Assert.IsFalse(dispatch.IsCompleted);
            Assert.IsFalse(fixture.Scopes.Single().Disposed);
        }
        finally
        {
            release.TrySetResult();
        }
        Assert.AreEqual(MessageDispatchOutcome.Cancelled, (await dispatch).Outcome);
        await stop.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, delivery.Requeues);
        Assert.AreEqual(0, delivery.Acknowledgements);
        Assert.AreEqual(MessageDeliveryFailureKind.Shutdown, fixture.Dispositions.Last!.Kind);
    }

    [TestMethod]
    public void ReservedReplyIngressCannotBecomeAnApplicationSubscriptionAndValidationInvokesNoFactories()
    {
        var codec = new PublicationNumberCodec();
        int resolutions = 0;
        var subscription = new MessageSubscriptionDefinition<int>(MessagingSystemSubscriptions.Replies, "Ledger", "events", codec,
            _ =>
            {
                resolutions++;
                throw new InvalidOperationException("No application factory belongs to passive validation.");
            }, (_, _, _, _) =>
            {
                resolutions++;
                return ValueTask.FromResult(new CapabilityTarget("target", "test"));
            }, ["rules.read"]);
        var snapshot = new MessagingSnapshot(new(), [new MessageRouteDefinition<int>("events", MessageKind.Event, codec)],
            [new("Messaging", "Ledger")], [subscription]);
        Assert.ThrowsExactly<InvalidOperationException>(snapshot.Validate);
        Assert.AreEqual(0, resolutions);
    }

    [TestMethod]
    public async Task OlderTrustedRetryObservesTheSecuredDispositionWithoutSchedulingOrExecutingAgain()
    {
        await using var fixture = new DispatchUnitFixture(retry: true);
        fixture.Transactions.Enqueue();
        fixture.Transactions.Enqueue();
        EncodedMessage message = fixture.Message();
        fixture.Store.Retained = new(new(fixture.Local.Application, fixture.Local.Environment, "A", message.Envelope.MessageId),
            message.Digest, fixture.Provenance, 3, MessageProcessingDisposition.RetryScheduled, 4, 1, DateTimeOffset.UnixEpoch,
            Guid.NewGuid());
        fixture.Store.ClaimDisposition = MessageClaimDisposition.RetryScheduled;
        var delivery = fixture.Delivery(message.CopyBytes());
        delivery.Headers = new("events", MessageRecordPurpose.Retry, 2, Guid.NewGuid(), 0, "A");
        Assert.AreEqual(MessageDispatchOutcome.Duplicate, (await fixture.Dispatcher.DispatchAsync(delivery)).Outcome);
        Assert.AreEqual(0, fixture.HandlerCalls);
        Assert.AreEqual(0, fixture.Dispositions.Calls);
        Assert.AreEqual(1, delivery.Acknowledgements);
        Assert.AreEqual(2, fixture.Transactions.CreatedTransactions.Count);
    }
}

// These use CommonTesting completion scripts and a controlled native projector, never SQL/broker evidence.
internal sealed class DispatchUnitFixture : IAsyncDisposable
{
    internal const int ByteCapacity = 100_000;
    internal readonly LocalApplicationIdentity Local = LocalApplicationIdentity.Capture();
    internal readonly ScriptedLocalTransactionFactory Transactions = new();
    internal readonly DispatchStore Store = new();
    internal readonly DispatchDispositions Dispositions = new();
    internal readonly SecurityTestSupport.RecordingAudit Audit = new();
    internal readonly List<DispatchScopeProbe> Scopes = [];
    internal readonly ServiceProvider Provider;
    internal readonly MessagingSnapshot Snapshot;
    internal readonly MessagingResourceBudget Resources;
    internal readonly MessageDispatcher Dispatcher;
    internal readonly MessagingInstrumentation? Observations;
    internal readonly MessageProvenance Provenance;
    internal readonly PublicationNumberCodec Codec = new();
    internal Func<MessageHandlerContext, int, CancellationToken, Task>? OnHandle;
    internal int HandlerCalls;
    private readonly string producerApplication;
    private readonly string producerEnvironment;

    internal DispatchUnitFixture(bool grant = true, bool retry = false, bool observe = false)
    {
        producerApplication = retry ? Local.Application : "caller-a";
        producerEnvironment = retry ? Local.Environment : "Development";
        Provenance = new("Broker", "fixture", SecurityTestSupport.Subject, "application-a", producerApplication, producerEnvironment);
        var security = SecurityTestSupport.Options();
        if (retry)
        {
            security.Profiles.Clear();
            security.Profiles.Add(new(SecurityTestSupport.Profile, SecurityProfileKind.Native, SecurityProfileRoles.Inbound,
                authenticationScheme: SecurityTestSupport.Profile,
                identityMappings: [new(SecurityTestSupport.Subject, producerApplication, producerEnvironment)]));
        }
        if (grant)
        {
            security.Grants.Add(new(SecurityTestSupport.Profile, "rules.read", Local.Application, Local.Environment, subject: SecurityTestSupport.Subject));
        }
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new ManualTimeProvider(SecurityTestSupport.Now));
        services.AddSingleton<ISecurityAuditSink>(Audit);
        services.AddScoped(_ =>
        {
            var scope = new DispatchScopeProbe();
            Scopes.Add(scope);
            return scope;
        });
        services.AddCisSecurity(security);
        services.AddCisSecurityProjection(SecurityTestSupport.Profile, provider =>
        {
            _ = provider.GetRequiredService<DispatchScopeProbe>();
            return new SecurityTestSupport.FixtureProjector(null, null);
        });
        Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var subscription = new MessageSubscriptionDefinition<int>("A", "Ledger", "events", Codec,
            _ => new DispatchHandler(async (context, value, token) =>
            {
                HandlerCalls++;
                if (OnHandle is { } handler)
                {
                    await handler(context, value, token);
                }
            }), (_, _, _, _) => ValueTask.FromResult(new CapabilityTarget(Local.Application, Local.Environment)),
            ["rules.read"], new() { RetryCapabilities = ["rules.read"] });
        Snapshot = new(new MessagingOptions
        {
            MaximumPayloadBytes = 16,
            MaximumConcurrentDeliveries = 1,
            MaximumOutstandingRequests = 1,
            PublisherConcurrency = 1,
            MaximumEncodedBufferBytes = ByteCapacity
        }, [new MessageRouteDefinition<int>("events", MessageKind.Event, Codec)], [new("Messaging", "Ledger")], [subscription]);
        Resources = new(Snapshot.Settings);
        Observations = observe ? new(Snapshot) : null;
        Dispatcher = new(Provider, Snapshot, Store, Transactions, Resources, Local, Dispositions, instrumentation: Observations);
    }

    internal EncodedMessage Message() => MessageWire.Encode(new(Guid.NewGuid(), MessageKind.Event, Codec.Contract,
        producerApplication, producerEnvironment, DateTimeOffset.UnixEpoch, "7"u8.ToArray()));

    internal DispatchDelivery Delivery(ReadOnlyMemory<byte> body) => new(body,
        new(SecurityTestSupport.Profile, SecurityTestSupport.NativeAuthentication(), Provenance));

    public async ValueTask DisposeAsync()
    {
        await Dispatcher.DisposeAsync();
        await Resources.DisposeAsync();
        await Provider.DisposeAsync();
    }
}

internal sealed class DispatchScopeProbe : IAsyncDisposable
{
    internal bool Disposed;
    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal sealed class DispatchHandler(Func<MessageHandlerContext, int, CancellationToken, Task> handler) : IMessageHandler<int>
{
    public Task HandleAsync(MessageHandlerContext context, int value, CancellationToken cancellationToken = default)
        => handler(context, value, cancellationToken);
}

internal sealed class DispatchDelivery(ReadOnlyMemory<byte> body, MessageNativeEvidence evidence) : IMessageDelivery
{
    internal Action? OnAcknowledge;
    internal Func<CancellationToken, Task>? OnRequeue;
    internal MessageDeliveryMetadata Headers = new("events");
    internal int Acknowledgements;
    internal int Requeues;
    internal int Stops;
    public ReadOnlyMemory<byte> Body => body;
    public string SubscriptionAlias => "A";
    public MessageDeliveryMetadata Metadata => Headers;
    public ValueTask<MessageNativeEvidence?> GetNativeEvidenceAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult<MessageNativeEvidence?>(evidence);
    public Task AcknowledgeAsync(CancellationToken cancellationToken = default)
    {
        OnAcknowledge?.Invoke();
        Acknowledgements++;
        return Task.CompletedTask;
    }
    public async Task RequeueAsync(CancellationToken cancellationToken = default)
    {
        Requeues++;
        if (OnRequeue is { } handler)
        {
            await handler(cancellationToken);
        }
    }
    public Task<bool> QuarantineMalformedAsync(string safeCode, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public void RequestConsumerStop(string safeCode) => Stops++;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class ObservedMessageMemory(byte[] bytes) : MemoryManager<byte>
{
    internal int SpanReads;
    public override Memory<byte> Memory => CreateMemory(bytes.Length);
    public override Span<byte> GetSpan()
    {
        SpanReads++;
        return bytes;
    }
    public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();
    public override void Unpin() { }
    protected override void Dispose(bool disposing) { }
}

internal sealed class DispatchDispositions : IMessageDeliveryDispositionCoordinator
{
    internal MessageFailedDelivery? Last;
    internal int Calls;
    public Task<MessageDispositionHandling> HandleAsync(MessageFailedDelivery failure, CancellationToken cancellationToken = default)
    {
        Calls++;
        Last = failure;
        MessageDispositionHandling result = failure.Kind is MessageDeliveryFailureKind.PersistenceUnknown or MessageDeliveryFailureKind.InfrastructureUnavailable
            ? new(MessageDispositionHandlingKind.Stop, failure.OriginalCompletion)
            : failure.Kind == MessageDeliveryFailureKind.Shutdown ? new(MessageDispositionHandlingKind.Requeue, failure.OriginalCompletion)
            : new(MessageDispositionHandlingKind.ResponsibilityCommitted,
                new(TransactionOutcome.Committed, TransactionEndReason.CommitRequested, true, false, false),
                failure.Kind == MessageDeliveryFailureKind.Rejected ? null : MessageProcessingDisposition.RetryScheduled);
        return Task.FromResult(result);
    }
}

internal sealed class DispatchStore : IMessagingStore
{
    internal MessageClaimDisposition ClaimDisposition = MessageClaimDisposition.Acquired;
    internal MessageProcessingRecord? Retained;
    internal ILocalTransactionContext? ClaimContext;
    internal ILocalTransactionContext? CompleteContext;
    internal int Claims;
    public Task<MessageProcessingClaim> ClaimProcessingAsync(ILocalTransactionContext context, MessageProcessingAttempt attempt, CancellationToken cancellationToken = default)
    {
        Claims++;
        ClaimContext = context;
        return Task.FromResult(new MessageProcessingClaim(ClaimDisposition, Retained ?? new(attempt.Key, attempt.Message.Digest,
            attempt.Provenance, attempt.DeliveryOrdinal, MessageProcessingDisposition.Ready, 1, attempt.Generation,
            DateTimeOffset.UnixEpoch, OperationBinding: attempt.OperationBinding)));
    }
    public Task<bool> CompleteProcessingAsync(ILocalTransactionContext context, MessageProcessingCompletion completion, CancellationToken cancellationToken = default)
    {
        CompleteContext = context;
        return Task.FromResult(true);
    }
    public Task<MessageProcessingRecord?> ReadProcessingAsync(ILocalTransactionContext context, MessageProcessingKey key, CancellationToken cancellationToken = default)
        => Task.FromResult(Retained);
    public Task<bool> AttachReplyAsync(ILocalTransactionContext context, MessageReplyAttachment attachment, CancellationToken cancellationToken = default)
        => Unexpected<bool>();
    public Task<MessageStageReceipt> StageAsync(ILocalTransactionContext context, MessageOutgoingRecord record, CancellationToken cancellationToken = default) => Unexpected<MessageStageReceipt>();
    public Task<IReadOnlyList<MessageReservation>> ReserveAsync(ILocalTransactionContext context, MessageReservationRequest request, CancellationToken cancellationToken = default) => Unexpected<IReadOnlyList<MessageReservation>>();
    public Task<EncodedMessage?> ReadReservedMessageAsync(ILocalTransactionContext context, MessageReservation reservation, CancellationToken cancellationToken = default) => Unexpected<EncodedMessage?>();
    public Task<bool> RecordPublicationAsync(ILocalTransactionContext context, MessagePublicationUpdate update, CancellationToken cancellationToken = default) => Unexpected<bool>();
    public Task<MessageSubmissionObservation> ReadSubmissionAsync(ILocalTransactionContext context, MessageStageReceipt receipt, CancellationToken cancellationToken = default) => Unexpected<MessageSubmissionObservation>();
    public Task<MessageDispositionResult> SecureDispositionAsync(ILocalTransactionContext context, MessageDispositionRequest request, CancellationToken cancellationToken = default) => Unexpected<MessageDispositionResult>();
    public Task RecordRejectionAsync(ILocalTransactionContext context, MessageRejection rejection, CancellationToken cancellationToken = default) => Unexpected<bool>();
    public Task<MessageRedriveResult> RedriveAsync(ILocalTransactionContext context, MessageRedriveTransition transition, CancellationToken cancellationToken = default) => Unexpected<MessageRedriveResult>();
    public Task<IReadOnlyList<MessageInspectionRecord>> InspectAsync(ILocalTransactionContext context, MessageInspectionRequest request, CancellationToken cancellationToken = default) => Unexpected<IReadOnlyList<MessageInspectionRecord>>();
    public Task<MessageMaintenanceCounts> MaintainAsync(ILocalTransactionContext context, MessageMaintenanceRequest request, CancellationToken cancellationToken = default) => Unexpected<MessageMaintenanceCounts>();
    public Task<MessageStoreObservation> ObserveAsync(ILocalTransactionContext context, MessageObservationRequest request, CancellationToken cancellationToken = default) => Unexpected<MessageStoreObservation>();
    private static Task<T> Unexpected<T>() => Task.FromException<T>(new InvalidOperationException("No other store path is selected by this dispatch unit substitute."));
}

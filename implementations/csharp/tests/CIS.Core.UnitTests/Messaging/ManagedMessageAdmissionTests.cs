using CIS.CommonTesting;
using CIS.Management;
using CIS.Messaging;
using CIS.Operations;
using CIS.Security;
using CIS.Tests.UnitTests.Security;
using CIS.Tests.UnitTests.Operations;
using CIS.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Messaging;

[TestClass]
[DoNotParallelize]
public sealed class ManagedMessageAdmissionTests
{
    [TestMethod]
    public async Task ClosedGateRequeuesWithoutClaimHandlerOrDurableFailureHandoff()
    {
        var admission = new ManualManagedWorkAdmission();
        await using var fixture = new DispatchUnitFixture(managedAdmission: admission);
        DispatchDelivery delivery = fixture.Delivery(fixture.Message().CopyBytes());

        MessageDispatchResult result = await fixture.Dispatcher.DispatchAsync(delivery);

        Assert.AreEqual("messaging.management_admission_closed", result.SafeCode);
        Assert.AreEqual(1, delivery.Requeues);
        Assert.AreEqual(0, delivery.Acknowledgements);
        Assert.AreEqual(0, fixture.Store.Claims);
        Assert.AreEqual(0, fixture.HandlerCalls);
        Assert.AreEqual(0, fixture.Transactions.CreatedTransactions.Count);
        Assert.AreEqual(0, fixture.Dispositions.Calls);
        Assert.IsTrue(fixture.Scopes.Single().Disposed);
    }

    [TestMethod]
    public async Task ClosureDuringInboxClaimRollsBackAndRequeuesBeforeCallback()
    {
        var admission = Ready();
        await using var fixture = new DispatchUnitFixture(managedAdmission: admission);
        fixture.Transactions.Enqueue();
        fixture.Store.OnClaim = () => Close(admission);
        DispatchDelivery delivery = fixture.Delivery(fixture.Message().CopyBytes());
        delivery.OnRequeue = _ =>
        {
            Assert.IsTrue(fixture.Transactions.CreatedTransactions.Single().DisposeRequested);
            Assert.IsTrue(fixture.Scopes.Single().Disposed);
            return Task.CompletedTask;
        };

        MessageDispatchResult result = await fixture.Dispatcher.DispatchAsync(delivery);

        Assert.AreEqual(TransactionOutcome.NotCommitted, result.Completion!.Outcome);
        Assert.AreEqual(1, fixture.Store.Claims);
        Assert.AreEqual(0, fixture.HandlerCalls);
        Assert.IsNull(fixture.Store.CompleteContext);
        Assert.AreEqual(1, delivery.Requeues);
        Assert.AreEqual(0, fixture.Dispositions.Calls);
    }

    [TestMethod]
    public async Task ClosureInsideAdmittedCallbackDoesNotCancelItsOriginalCommit()
    {
        var admission = Ready();
        await using var fixture = new DispatchUnitFixture(managedAdmission: admission);
        fixture.Transactions.Enqueue();
        fixture.OnHandle = (context, _, token) =>
        {
            Close(admission);
            Assert.IsFalse(token.IsCancellationRequested);
            Assert.IsFalse(context.Transaction.IsRollbackOnly);
            return Task.CompletedTask;
        };
        DispatchDelivery delivery = fixture.Delivery(fixture.Message().CopyBytes());

        MessageDispatchResult result = await fixture.Dispatcher.DispatchAsync(delivery);

        Assert.AreEqual(TransactionOutcome.Committed, result.Completion!.Outcome);
        Assert.AreEqual(1, fixture.HandlerCalls);
        Assert.AreEqual(1, delivery.Acknowledgements);
        Assert.AreEqual(0, delivery.Requeues);
        Assert.IsFalse(admission.TryAdmit());
    }

    [TestMethod]
    public async Task ClosedGateKeepsNativeReplyIngressAvailableWithoutNewInboxClaim()
    {
        var admission = new ManualManagedWorkAdmission();
        await using var fixture = new DispatchUnitFixture(requests: true, managedAdmission: admission);
        int replies = 0;
        fixture.Replies!.OnReceive = (reply, services, _) =>
        {
            Assert.AreEqual(MessageKind.Reply, reply.Envelope.Kind);
            Assert.IsNotNull(services.GetRequiredService<ICallerContextAccessor>().Current);
            replies++;
            return Task.FromResult(MessageReplyHandling.Acknowledge);
        };
        EncodedMessage encoded = MessageWire.Encode(new(Guid.NewGuid(), MessageKind.Reply, fixture.Codec.Contract,
            "caller-a", "Development", SecurityTestSupport.Now, "7"u8.ToArray(),
            requestAttemptId: Guid.NewGuid(), inReplyToMessageId: Guid.NewGuid()));
        DispatchDelivery delivery = fixture.Delivery(encoded.CopyBytes());
        delivery.Alias = MessagingSystemSubscriptions.Replies;

        await fixture.Dispatcher.DispatchAsync(delivery);

        Assert.AreEqual(1, replies);
        Assert.AreEqual(1, delivery.Acknowledgements);
        Assert.AreEqual(0, delivery.Requeues);
        Assert.AreEqual(0, fixture.Store.Claims);
        Assert.AreEqual(0L, admission.AdmissionChecks);
    }

    [TestMethod]
    public async Task ClosureWhileActualOperationClaimWaitsPreventsFreshHandlerAndRollsBackOriginalInboxBeforeRequeue()
    {
        var admission = Ready();
        await using var fixture = new DispatchUnitFixture(managedAdmission: admission, durable: true);
        fixture.Transactions.Enqueue();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.OperationJournal!.Claim = async (_, _, _, token) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            return new(OperationClaimDisposition.Acquired);
        };
        DispatchDelivery delivery = fixture.Delivery(fixture.Message().CopyBytes());
        delivery.OnRequeue = _ =>
        {
            Assert.IsTrue(fixture.Transactions.CreatedTransactions.Single().DisposeRequested);
            Assert.IsTrue(fixture.Scopes.Single().Disposed);
            return Task.CompletedTask;
        };
        Task<MessageDispatchResult> pending = fixture.Dispatcher.DispatchAsync(delivery);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(1, fixture.Store.Claims);
            Close(admission);
            release.TrySetResult();

            MessageDispatchResult result = await pending.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.AreEqual("messaging.management_admission_closed", result.SafeCode);
            Assert.AreEqual(TransactionOutcome.NotCommitted, result.Completion!.Outcome);
            Assert.AreSame(fixture.Store.ClaimContext, fixture.OperationJournal.ClaimedContext);
            Assert.IsTrue(fixture.Store.ClaimContext!.IsRollbackOnly);
            Assert.IsTrue(fixture.Transactions.CreatedTransactions.Single().RollbackRequested);
            Assert.AreEqual(0, fixture.HandlerCalls);
            Assert.AreEqual(0, fixture.HandlerConstructions);
            Assert.AreEqual(0, fixture.OperationJournal.Writes);
            Assert.IsNull(fixture.Store.CompleteContext);
            Assert.AreEqual(0, fixture.Replies!.Staged);
            Assert.AreEqual(1, delivery.Requeues);
            Assert.AreEqual(0, delivery.Acknowledgements);
            Assert.AreEqual(0, fixture.Dispositions.Calls);
            Assert.IsNull(result.Fault, "Expected gate deferral must not replace actual cleanup with a private control exception.");
        }
        finally
        {
            release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [TestMethod]
    public async Task MatchingCommittedOperationReplayAfterClaimWaitSettlesItsReplyWithoutAnotherHandlerDespiteGateClosure()
    {
        var admission = Ready();
        await using var fixture = new DispatchUnitFixture(managedAdmission: admission, durable: true);
        fixture.Transactions.Enqueue();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.OperationJournal!.Claim = async (_, identity, fingerprint, token) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            return new(OperationClaimDisposition.Existing, RuntimeJournal.Row(identity, fingerprint,
                new(DispatchUnitFixture.DurableResultContract, OperationBusinessDecision.Succeeded, fixture.Codec.Encode(41))));
        };
        DispatchDelivery delivery = fixture.Delivery(fixture.Message().CopyBytes());
        delivery.OnAcknowledge = () =>
        {
            Assert.IsTrue(fixture.Transactions.CreatedTransactions.Single().DisposeRequested);
            Assert.IsTrue(fixture.Scopes.Single().Disposed);
        };
        Task<MessageDispatchResult> pending = fixture.Dispatcher.DispatchAsync(delivery);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Close(admission);
            release.TrySetResult();

            MessageDispatchResult result = await pending.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.AreEqual(TransactionOutcome.Committed, result.Completion!.Outcome);
            Assert.AreEqual(0, fixture.HandlerCalls);
            Assert.AreEqual(0, fixture.HandlerConstructions);
            Assert.AreEqual(0, fixture.OperationJournal.Writes);
            Assert.AreSame(fixture.Store.ClaimContext, fixture.OperationJournal.ClaimedContext);
            Assert.AreSame(fixture.Store.ClaimContext, fixture.Store.CompleteContext);
            Assert.AreEqual(1, fixture.Replies!.Staged);
            Assert.AreEqual(MessageProtocolDisposition.Completed, fixture.Replies.LastDisposition);
            Assert.AreEqual(41, fixture.Replies.LastNumber);
            Assert.IsTrue(fixture.Transactions.CreatedTransactions.Single().CommitRequested);
            Assert.AreEqual(1, delivery.Acknowledgements);
            Assert.AreEqual(0, delivery.Requeues);
            Assert.AreEqual(0, fixture.Dispositions.Calls);
            Assert.IsFalse(admission.TryAdmit());
        }
        finally
        {
            release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static ManualManagedWorkAdmission Ready()
    {
        var admission = new ManualManagedWorkAdmission();
        admission.Set(new(ManagedWorkAdmissionState.Ready, true, 1));
        return admission;
    }

    private static void Close(ManualManagedWorkAdmission admission)
        => admission.Set(new(ManagedWorkAdmissionState.ContinuingOnly, false, 2));
}

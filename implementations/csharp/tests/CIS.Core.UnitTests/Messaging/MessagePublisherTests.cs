using CIS.Messaging;
using CIS.Transactions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Messaging;

[TestClass]
[DoNotParallelize]
public sealed class MessagePublisherTests
{
    [TestMethod]
    [DataRow(TransactionOutcome.Unknown)]
    [DataRow(TransactionOutcome.NotCommitted)]
    public async Task UnconfirmedReservationCommitCannotStartBodyLoadingOrBrokerPublication(TransactionOutcome outcome)
    {
        await using var fixture = new PublicationUnitFixture();
        fixture.Script(outcome);
        MessagePumpResult result = await fixture.Publisher.PumpAsync();
        Assert.AreEqual(outcome, result.ReservationCompletion!.Outcome);
        Assert.AreEqual(0, result.Attempted);
        Assert.AreEqual(0, fixture.Store.ReadCalls);
        Assert.AreEqual(0, fixture.Broker.Calls);
        Assert.IsNull(fixture.Store.LastUpdate);
    }

    [TestMethod]
    public async Task BrokerWaitHasNoOpenDatabaseOwnerAndAnUncertainStateCommitDoesNotRewriteNativeConfirmation()
    {
        await using var fixture = new PublicationUnitFixture();
        fixture.Script(TransactionOutcome.Committed, TransactionOutcome.Committed, TransactionOutcome.Unknown);
        fixture.Broker.OnPublish = (_, _) =>
        {
            Assert.AreEqual(2, fixture.Transactions.CreatedTransactions.Count);
            Assert.IsTrue(fixture.Transactions.CreatedTransactions.All(owner => owner.Status == LocalTransactionStatus.Completed));
            return Task.FromResult(new MessagePublicationResult(MessagePublicationDisposition.Confirmed));
        };
        MessagePumpResult result = await fixture.Publisher.PumpAsync();
        Assert.AreEqual(1, result.Confirmed);
        MessagePublicationAttempt attempt = result.Attempts.Single();
        Assert.IsTrue(attempt.UpdatePrepared);
        Assert.AreEqual(TransactionOutcome.Unknown, attempt.SettlementCompletion!.Outcome);
        Assert.IsTrue(fixture.Transactions.CreatedTransactions.All(owner => owner.DisposeRequested));
        CollectionAssert.AreEqual(fixture.Store.Body.CopyBytes(), fixture.Broker.Last!.Message.CopyBytes());
    }

    [TestMethod]
    public async Task ByteAdmissionPrecedesMaterializationAndAStaleReservationNeverTransmits()
    {
        await using var fixture = new PublicationUnitFixture();
        fixture.Script(TransactionOutcome.Committed, TransactionOutcome.Committed,
            TransactionOutcome.Committed, TransactionOutcome.Committed);
        using IDisposable held = await fixture.Resources.AcquireAsync(PublicationUnitFixture.ByteCapacity);
        MessagePumpResult deferred = await fixture.Publisher.PumpAsync();
        Assert.IsTrue(deferred.Attempts.Single().Deferred);
        Assert.AreEqual(0, fixture.Store.ReadCalls);
        Assert.AreEqual(0, fixture.Broker.Calls);
        Assert.AreEqual(MessagePublicationFailure.Transient, fixture.Store.LastUpdate!.Result.Failure);
        held.Dispose();
        fixture.Store.MissingBody = true;
        MessagePumpResult stale = await fixture.Publisher.PumpAsync();
        Assert.IsTrue(stale.Attempts.Single().Stale);
        Assert.AreEqual(1, fixture.Store.ReadCalls);
        Assert.AreEqual(0, fixture.Broker.Calls);
    }

    [TestMethod]
    [DataRow(MessagePublicationDisposition.Unknown, MessagePublicationFailure.Transient)]
    [DataRow(MessagePublicationDisposition.Rejected, MessagePublicationFailure.Unroutable)]
    [DataRow(MessagePublicationDisposition.Rejected, MessagePublicationFailure.Configuration)]
    public async Task ActualNativeClassificationAndBoundedBackoffReachTheFreshConditionalSettlement(
        MessagePublicationDisposition disposition, MessagePublicationFailure failure)
    {
        await using var fixture = new PublicationUnitFixture();
        fixture.Script(TransactionOutcome.Committed, TransactionOutcome.Committed, TransactionOutcome.Committed);
        fixture.Broker.OnPublish = (_, _) => Task.FromResult(new MessagePublicationResult(disposition, failure, "messaging.test"));
        MessagePumpResult result = await fixture.Publisher.PumpAsync();
        Assert.AreEqual(disposition, result.Attempts.Single().Result!.Disposition);
        Assert.AreEqual(failure, fixture.Store.LastUpdate!.Result.Failure);
        if (failure == MessagePublicationFailure.Transient)
        {
            Assert.IsTrue(fixture.Store.LastUpdate.RetryDelay > TimeSpan.Zero);
            Assert.IsTrue(fixture.Store.LastUpdate.RetryDelay <= TimeSpan.FromSeconds(60));
        }
        else
        {
            Assert.AreEqual(TimeSpan.Zero, fixture.Store.LastUpdate.RetryDelay);
        }
        Assert.AreEqual(TransactionOutcome.Committed, result.Attempts.Single().SettlementCompletion!.Outcome);
    }

    [TestMethod]
    public async Task LostConfirmationRetriesTheExactImmutableEnvelopeAndAStaleFenceIsReportedSeparately()
    {
        await using var fixture = new PublicationUnitFixture();
        fixture.Script(Enumerable.Repeat(TransactionOutcome.Committed, 6).ToArray());
        EncodedMessage? first = null;
        fixture.Broker.OnPublish = (publication, _) =>
        {
            first = publication.Message;
            return Task.FromResult(new MessagePublicationResult(MessagePublicationDisposition.Unknown, MessagePublicationFailure.Transient));
        };
        Assert.AreEqual(1, (await fixture.Publisher.PumpAsync()).Unknown);
        fixture.Store.UpdateResult = false;
        fixture.Broker.OnPublish = (publication, _) =>
        {
            Assert.AreEqual(first!.Envelope.MessageId, publication.Message.Envelope.MessageId);
            Assert.AreEqual(first.Digest, publication.Message.Digest);
            CollectionAssert.AreEqual(first.CopyBytes(), publication.Message.CopyBytes());
            return Task.FromResult(new MessagePublicationResult(MessagePublicationDisposition.Confirmed));
        };
        MessagePublicationAttempt second = (await fixture.Publisher.PumpAsync()).Attempts.Single();
        Assert.IsTrue(second.Stale);
        Assert.IsFalse(second.UpdatePrepared);
        Assert.AreEqual(MessagePublicationDisposition.Confirmed, second.Result!.Disposition);
    }

    [TestMethod]
    public async Task MonotonicTimeoutAndStopKeepBorrowedBytesUntilActualNativeWorkAndCleanupSettle()
    {
        await using var fixture = new PublicationUnitFixture();
        fixture.Script(TransactionOutcome.Committed, TransactionOutcome.Committed, TransactionOutcome.Committed);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Broker.OnPublish = async (_, token) =>
        {
            using CancellationTokenRegistration registration = token.Register(() => cancelled.TrySetResult());
            entered.TrySetResult();
            await release.Task;
            token.ThrowIfCancellationRequested();
            return new(MessagePublicationDisposition.Confirmed);
        };
        Task<MessagePumpResult> pumping = fixture.Publisher.PumpAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Clock.SetUtcNow(DateTimeOffset.UnixEpoch.AddDays(365));
        Assert.IsFalse(cancelled.Task.IsCompleted);
        fixture.Clock.Advance(TimeSpan.FromSeconds(10));
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task stopping = fixture.Publisher.StopAsync();
        try
        {
            Assert.IsFalse(pumping.IsCompleted);
            Assert.IsFalse(stopping.IsCompleted);
            Assert.IsFalse(fixture.Resources.TryAcquireBytes(PublicationUnitFixture.ByteCapacity, out _));
        }
        finally
        {
            release.TrySetResult();
        }
        MessagePumpResult result = await pumping.WaitAsync(TimeSpan.FromSeconds(5));
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, result.Unknown);
        Assert.IsFalse(fixture.Store.SettlementToken.IsCancellationRequested);
        Assert.AreEqual(TransactionOutcome.Committed, result.Attempts.Single().SettlementCompletion!.Outcome);
        using IDisposable allBytes = await fixture.Resources.AcquireAsync(PublicationUnitFixture.ByteCapacity);
    }

    [TestMethod]
    public async Task ProgrammingFaultsRemainOriginalAfterBestEffortUnknownSettlement()
    {
        await using var fixture = new PublicationUnitFixture();
        fixture.Script(TransactionOutcome.Committed, TransactionOutcome.Committed, TransactionOutcome.Committed);
        var original = new InvalidOperationException("application.publisher_fault");
        fixture.Broker.OnPublish = (_, _) => Task.FromException<MessagePublicationResult>(original);
        InvalidOperationException observed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Publisher.PumpAsync());
        Assert.AreSame(original, observed);
        Assert.AreEqual(MessagePublicationDisposition.Unknown, fixture.Store.LastUpdate!.Result.Disposition);
        Assert.IsTrue(fixture.Transactions.CreatedTransactions.All(owner => owner.Status == LocalTransactionStatus.Completed));
    }

    [TestMethod]
    public async Task WakeIsCoalescedAndStoppedPublisherHasNoNewDatabaseWork()
    {
        await using var fixture = new PublicationUnitFixture();
        fixture.Publisher.Wake();
        fixture.Publisher.Wake();
        await fixture.Publisher.WaitForWorkAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Publisher.StopAsync();
        Assert.IsTrue((await fixture.Publisher.PumpAsync()).Unavailable);
        Assert.AreEqual(0, fixture.Transactions.CreatedTransactions.Count);
    }
}

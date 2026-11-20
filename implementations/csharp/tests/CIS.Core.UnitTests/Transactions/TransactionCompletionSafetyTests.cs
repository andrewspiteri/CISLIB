using CIS.TestSupport.Transactions;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using CIS.Transactions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Transactions;

[TestClass]
public sealed class TransactionCompletionSafetyTests
{
    [TestMethod]
    public async Task CancelledCommitBeforeNativeInvocationRollsBackWithAnIndependentToken()
    {
        using var source = new RecordingTransactionDataSource();
        using ServiceProvider provider = Provider(source);
        await using ILocalTransaction owner = await provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");
        using var caller = new CancellationTokenSource();
        caller.Cancel();

        TransactionCompletion completed = await owner.CommitAsync(caller.Token);

        RecordingNativeTransaction native = source.Connections.Single().Transactions.Single();
        Assert.AreEqual(TransactionOutcome.NotCommitted, completed.Outcome);
        Assert.AreEqual(TransactionEndReason.Cancellation, completed.EndReason);
        Assert.AreEqual("transaction.cancelled_before_commit", completed.Error?.Code);
        Assert.IsFalse(completed.CommitAttempted);
        Assert.IsTrue(completed.RollbackAttempted);
        Assert.IsTrue(completed.RollbackConfirmed);
        Assert.AreEqual(0, native.CommitCalls);
        Assert.AreEqual(1, native.RollbackCalls);
        Assert.IsTrue(native.RollbackToken.CanBeCanceled);
        Assert.IsFalse(native.RollbackToken.IsCancellationRequested);
        Assert.AreNotEqual(caller.Token, native.RollbackToken);
        Assert.AreEqual(1, native.DisposeCalls);
        Assert.AreEqual(1, source.Connections.Single().DisposeCalls);
        Assert.AreEqual(0, source.DisposeCalls);
    }

    [TestMethod]
    public async Task RollbackOnlyRefusalPrecedesAnAlreadyCancelledCommitToken()
    {
        using var source = new RecordingTransactionDataSource();
        using ServiceProvider provider = Provider(source);
        await using ILocalTransaction owner = await provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");
        using var caller = new CancellationTokenSource();
        owner.Context.MarkRollbackOnly("business.requires_rollback");
        caller.Cancel();

        TransactionCompletion completed = await owner.CommitAsync(caller.Token);

        Assert.AreEqual(TransactionOutcome.NotCommitted, completed.Outcome);
        Assert.AreEqual(TransactionEndReason.RollbackOnly, completed.EndReason);
        Assert.AreEqual("transaction.rollback_only", completed.Error?.Code);
        Assert.AreEqual("business.requires_rollback", owner.Context.RollbackReasonCode);
        Assert.IsFalse(completed.CommitAttempted);
        Assert.IsTrue(completed.RollbackConfirmed);
        Assert.AreEqual(0, source.Connections.Single().Transactions.Single().CommitCalls);
        Assert.AreEqual(1, source.Connections.Single().Transactions.Single().RollbackCalls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FailedNativeCommitInvocationIsUnknownEvenWhenRollbackIsAcknowledged(bool nativeCancellation)
    {
        using var caller = new CancellationTokenSource();
        var original = new InvalidOperationException("private synchronous native commit fixture");
        using var source = new RecordingTransactionDataSource
        {
            ConnectionCreated = connection => connection.TransactionCreated = transaction =>
                transaction.CommitHandler = token =>
                {
                    Assert.AreEqual(caller.Token, token);
                    if (nativeCancellation)
                    {
                        caller.Cancel();
                        return Task.FromCanceled(token);
                    }
                    throw original;
                }
        };
        using ServiceProvider provider = Provider(source);
        await using ILocalTransaction owner = await provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");

        TransactionCompletion completed = await owner.CommitAsync(caller.Token);

        RecordingNativeTransaction native = source.Connections.Single().Transactions.Single();
        Assert.AreEqual(TransactionOutcome.Unknown, completed.Outcome);
        Assert.AreEqual(TransactionEndReason.CommitRequested, completed.EndReason);
        Assert.AreEqual("transaction.commit_outcome_unknown", completed.Error?.Code);
        Assert.AreEqual(TransactionErrorPhase.Commit, completed.Error?.Phase);
        Assert.IsTrue(completed.CommitAttempted);
        Assert.IsTrue(completed.RollbackAttempted);
        Assert.IsTrue(completed.RollbackConfirmed);
        Assert.HasCount(0, completed.CleanupErrors);
        Assert.AreEqual(1, native.CommitCalls);
        Assert.AreEqual(1, native.RollbackCalls);
        Assert.AreNotEqual(caller.Token, native.RollbackToken);
        Assert.IsFalse(native.RollbackToken.IsCancellationRequested);
        Assert.IsFalse(completed.Error!.Message.Contains("private", StringComparison.Ordinal));
        CollectionAssert.AreEqual(new[] { "open", "begin", "commit", "rollback", "transaction.dispose", "connection.dispose" },
            source.Events.ToArray());
        Assert.AreSame(completed, owner.Completion);
    }

    [TestMethod]
    public async Task NativeCommitSuccessStaysCommittedWhenItsHandlerCancelsTheCaller()
    {
        using var caller = new CancellationTokenSource();
        using var source = new RecordingTransactionDataSource
        {
            ConnectionCreated = connection => connection.TransactionCreated = transaction =>
                transaction.CommitHandler = token =>
                {
                    Assert.AreEqual(caller.Token, token);
                    caller.Cancel();
                    return Task.CompletedTask;
                }
        };
        using ServiceProvider provider = Provider(source);
        await using ILocalTransaction owner = await provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");

        TransactionCompletion completed = await owner.CommitAsync(caller.Token);

        Assert.IsTrue(caller.IsCancellationRequested);
        Assert.AreEqual(TransactionOutcome.Committed, completed.Outcome);
        Assert.AreEqual(TransactionEndReason.CommitRequested, completed.EndReason);
        Assert.IsTrue(completed.CommitAttempted);
        Assert.IsFalse(completed.RollbackAttempted);
        Assert.IsNull(completed.Error);
        Assert.AreEqual(1, source.Connections.Single().Transactions.Single().CommitCalls);
        Assert.AreEqual(0, source.Connections.Single().Transactions.Single().RollbackCalls);
        CollectionAssert.AreEqual(new[] { "open", "begin", "commit", "transaction.dispose", "connection.dispose" }, source.Events.ToArray());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EscapingFaultPreservesItsIdentityAndDoesNotBecomeAnUnrelatedCancellation(bool unrelatedCancellation)
    {
        using var supplied = new CancellationTokenSource();
        using var foreign = new CancellationTokenSource();
        foreign.Cancel();
        Exception original = unrelatedCancellation
            ? new OperationCanceledException("foreign operation fixture", foreign.Token)
            : new InvalidOperationException("original application fixture fault");
        CancellationToken workToken = unrelatedCancellation ? CancellationToken.None : supplied.Token;
        using var source = new RecordingTransactionDataSource();
        using ServiceProvider provider = Provider(source);
        await using ILocalTransaction owner = await provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");

        Exception observed = await Assert.ThrowsAsync<Exception>(() => owner.Context.UseAdoNetAsync((_, _, token) =>
        {
            Assert.AreEqual(workToken, token);
            if (!unrelatedCancellation)
            {
                supplied.Cancel();
            }
            return Task.FromException(original);
        }, workToken));

        Assert.AreSame(original, observed);
        Assert.AreEqual(!unrelatedCancellation, supplied.IsCancellationRequested);
        Assert.IsTrue(owner.Context.IsRollbackOnly);
        Assert.AreEqual("transaction.work_failed", owner.Context.RollbackReasonCode);
        TransactionCompletion completed = await owner.CommitAsync();
        Assert.AreEqual(TransactionOutcome.NotCommitted, completed.Outcome);
        Assert.AreEqual(TransactionEndReason.RollbackOnly, completed.EndReason);
        Assert.AreEqual(0, source.Connections.Single().Transactions.Single().CommitCalls);
        Assert.IsTrue(completed.RollbackConfirmed);
    }

    [TestMethod]
    public async Task BeginTokenCancellationAfterPublicationDoesNotCancelTheOwnerLifetime()
    {
        using var begin = new CancellationTokenSource();
        using var source = new RecordingTransactionDataSource();
        using ServiceProvider provider = Provider(source);
        await using ILocalTransaction owner = await provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger", begin.Token);
        begin.Cancel();

        int value = await owner.Context.UseAdoNetAsync((_, _, token) =>
        {
            Assert.AreEqual(CancellationToken.None, token);
            Assert.IsFalse(token.IsCancellationRequested);
            return Task.FromResult(17);
        });
        TransactionCompletion completed = await owner.CommitAsync();

        Assert.AreEqual(17, value);
        Assert.IsFalse(owner.Context.IsRollbackOnly);
        Assert.AreEqual(TransactionOutcome.Committed, completed.Outcome);
        Assert.AreEqual(CancellationToken.None, source.Connections.Single().Transactions.Single().CommitToken);
        Assert.AreEqual(0, source.Connections.Single().Transactions.Single().RollbackCalls);
    }

    [TestMethod]
    public async Task WorkTokenCancellationAfterItsCompletedCheckpointDoesNotPoisonLaterWorkOrCommit()
    {
        using var work = new CancellationTokenSource();
        using var source = new RecordingTransactionDataSource();
        using ServiceProvider provider = Provider(source);
        await using ILocalTransaction owner = await provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");
        int first = await owner.Context.UseAdoNetAsync((_, _, token) =>
        {
            Assert.AreEqual(work.Token, token);
            return Task.FromResult(1);
        }, work.Token);
        work.Cancel();

        int second = await owner.Context.UseAdoNetAsync((_, _, token) =>
        {
            Assert.AreEqual(CancellationToken.None, token);
            return Task.FromResult(2);
        });
        TransactionCompletion completed = await owner.CommitAsync();

        Assert.AreEqual(1, first);
        Assert.AreEqual(2, second);
        Assert.IsFalse(owner.Context.IsRollbackOnly);
        Assert.AreEqual(TransactionOutcome.Committed, completed.Outcome);
        Assert.AreEqual(1, source.Connections.Single().Transactions.Single().CommitCalls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MarkingBeforeOrAfterCompletionClaimCannotLoseAnAcceptedRollbackReason(bool markBeforeClaim)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var source = new RecordingTransactionDataSource
        {
            ConnectionCreated = connection => connection.TransactionCreated = transaction =>
            {
                if (markBeforeClaim)
                {
                    transaction.RollbackHandler = Hold;
                }
                else
                {
                    transaction.CommitHandler = Hold;
                }
            }
        };
        using ServiceProvider provider = Provider(source);
        await using ILocalTransaction owner = await provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");
        if (markBeforeClaim)
        {
            owner.Context.MarkRollbackOnly("accepted_before_claim");
        }
        Task<TransactionCompletion>? pending = null;
        TransactionCompletion? completed = null;
        try
        {
            pending = owner.CommitAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(LocalTransactionStatus.Completing, owner.Status);
            Assert.IsNull(owner.Completion);
            Assert.IsFalse(pending.IsCompleted);
            Assert.AreEqual(markBeforeClaim, owner.Context.IsRollbackOnly);
            Assert.ThrowsExactly<InvalidOperationException>(() => owner.Context.MarkRollbackOnly("rejected_after_claim"));
        }
        finally
        {
            release.TrySetResult();
            if (pending is not null)
            {
                completed = await pending;
            }
        }
        Assert.IsNotNull(completed);
        RecordingNativeTransaction native = source.Connections.Single().Transactions.Single();
        Assert.AreEqual(markBeforeClaim ? 0 : 1, native.CommitCalls);
        Assert.AreEqual(markBeforeClaim ? 1 : 0, native.RollbackCalls);
        Assert.AreEqual(markBeforeClaim ? TransactionOutcome.NotCommitted : TransactionOutcome.Committed, completed.Outcome);
        Assert.AreEqual(markBeforeClaim ? TransactionEndReason.RollbackOnly : TransactionEndReason.CommitRequested, completed.EndReason);
        Assert.AreEqual(markBeforeClaim ? "accepted_before_claim" : null, owner.Context.RollbackReasonCode);
        Assert.AreSame(completed, owner.Completion);

        async Task Hold(CancellationToken _)
        {
            entered.TrySetResult();
            await release.Task;
        }
    }

    private static ServiceProvider Provider(RecordingTransactionDataSource source)
    {
        var services = new ServiceCollection();
        services.AddCisAdoNetDatabase("Ledger", _ => source, new()
        {
            SupportedIsolationLevels = [TransactionIsolation.ReadCommitted]
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}

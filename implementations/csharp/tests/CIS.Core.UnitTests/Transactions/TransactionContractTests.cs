using System.Data.Common;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Transactions;

[TestClass]
public sealed class TransactionContractTests
{
    [TestMethod]
    public void CompletionCopiesBoundedCleanupAndKeepsPersistenceSeparate()
    {
        var error = new TransactionError(TransactionErrorPhase.TransactionDispose,
            "transaction.transaction_disposal_failed", "Safe predefined cleanup failure.");
        TransactionError[] errors = [error];
        var result = new TransactionCompletion(TransactionOutcome.Committed, TransactionEndReason.CommitRequested,
            commitAttempted: true, rollbackAttempted: false, rollbackConfirmed: false, cleanupErrors: errors,
            durationSeconds: 0.25);
        errors[0] = new(TransactionErrorPhase.ConnectionDispose, "different", "Different safe error.");
        Assert.AreSame(error, result.CleanupErrors.Single());
        Assert.AreEqual(TransactionOutcome.Committed, result.Outcome);
        Assert.AreEqual(0.25, result.DurationSeconds);
        Assert.IsNull(result.Error);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<TransactionError>)result.CleanupErrors).Clear());
        Assert.ThrowsExactly<ArgumentException>(() => new TransactionCompletion(TransactionOutcome.NotCommitted,
            TransactionEndReason.RollbackRequested, false, true, false, cleanupErrors: [error, error, error, error]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TransactionCompletion(TransactionOutcome.NotCommitted,
            TransactionEndReason.RollbackRequested, false, true, false, durationSeconds: double.NaN));
        Assert.ThrowsExactly<ArgumentException>(() => new TransactionError(TransactionErrorPhase.Commit,
            "bad\ncode", "Safe message."));
        Assert.ThrowsExactly<ArgumentException>(() => new TransactionError(TransactionErrorPhase.Commit,
            "code", new string(' ', 513)));
    }

    [TestMethod]
    public void PortableOnlyContextRejectsNativeUseBeforeInvokingApplicationWork()
    {
        ILocalTransactionContext context = new PortableContext();
        bool entered = false;
        Assert.ThrowsExactly<NotSupportedException>(() => context.UseAdoNetAsync((_, _, _) =>
        {
            entered = true;
            return Task.CompletedTask;
        }));
        Assert.IsFalse(entered);
    }

    [TestMethod]
    public async Task NativeExtensionForwardsThroughThePublicMockableCapability()
    {
        var context = new NativeContext();
        using var cancellation = new CancellationTokenSource();
        Func<DbConnection, DbTransaction, CancellationToken, Task<int>> work = (_, _, _) =>
            throw new AssertFailedException("The mock capability controls invocation, without a concrete runtime cast.");
        int value = await ((ILocalTransactionContext)context).UseAdoNetAsync(work, cancellation.Token);
        Assert.AreEqual(7, value);
        Assert.AreSame(work, context.Received);
        Assert.AreEqual(cancellation.Token, context.Token);
    }

    private class PortableContext : ILocalTransactionContext
    {
        public string DatabaseName => "Ledger";
        public TransactionIsolation Isolation => TransactionIsolation.ReadCommitted;
        public bool IsRollbackOnly => RollbackReasonCode is not null;
        public string? RollbackReasonCode
        {
            get; private set;
        }
        public void MarkRollbackOnly(string reasonCode) => RollbackReasonCode ??= reasonCode;
    }

    private sealed class NativeContext : PortableContext, IAdoNetTransactionContext
    {
        internal Delegate? Received
        {
            get; private set;
        }
        internal CancellationToken Token
        {
            get; private set;
        }
        public Task UseAsync(Func<DbConnection, DbTransaction, CancellationToken, Task> work,
            CancellationToken cancellationToken = default) => throw new AssertFailedException("The generic overload was selected.");
        public Task<T> UseAsync<T>(Func<DbConnection, DbTransaction, CancellationToken, Task<T>> work,
            CancellationToken cancellationToken = default)
        {
            Received = work;
            Token = cancellationToken;
            return Task.FromResult((T)(object)7);
        }
    }
}

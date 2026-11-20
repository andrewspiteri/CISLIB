using CIS.CommonTesting;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.CommonTesting;

[TestClass]
public sealed class ScriptedLocalTransactionFactoryTests
{
    [TestMethod]
    public async Task StoresReceiveTheSameSeparateContextAndRecordsRemainInspectable()
    {
        var factory = new ScriptedLocalTransactionFactory();
        factory.Enqueue(defaultIsolation: TransactionIsolation.Serializable);
        var first = new PortableStore();
        var second = new PortableStore();
        IReadOnlyList<ScriptedLocalTransaction> before = factory.CreatedTransactions;
        ILocalTransaction transaction = await factory.BeginAsync("Ledger.Database");
        await first.WriteAsync(transaction.Context);
        await second.WriteAsync(transaction.Context);

        Assert.AreSame(transaction.Context, first.Received);
        Assert.AreSame(first.Received, second.Received);
        Assert.AreNotSame<object>(transaction, transaction.Context);
        Assert.AreEqual("Ledger.Database", transaction.Context.DatabaseName);
        Assert.AreEqual(TransactionIsolation.Serializable, transaction.Context.Isolation);
        Assert.IsEmpty(before);
        Assert.HasCount(1, factory.CreatedTransactions);
        ScriptedLocalTransaction recording = factory.CreatedTransactions.Single();
        Assert.AreSame(transaction, recording);
        Assert.AreEqual(LocalTransactionStatus.Active, recording.Status);
        Assert.IsNull(recording.Completion);

        TransactionCompletion completion = await transaction.CommitAsync();
        Assert.AreEqual(TransactionOutcome.Committed, completion.Outcome);
        Assert.IsNull(completion.DurationSeconds);
        Assert.IsTrue(recording.CommitRequested);
        Assert.IsFalse(recording.RollbackRequested);
        Assert.IsFalse(recording.DisposeRequested);
        await transaction.DisposeAsync();
        await transaction.DisposeAsync();
        Assert.IsTrue(recording.DisposeRequested);
        Assert.AreSame(completion, recording.Completion);
        Assert.AreEqual(LocalTransactionStatus.Completed, recording.Status);
        Assert.AreEqual("Ledger.Database", recording.Context.DatabaseName);
    }

    [TestMethod]
    public async Task BeginFailurePreservesItsOriginalAndExplicitIsolationOverridesOnlyThatOwner()
    {
        var factory = new ScriptedLocalTransactionFactory();
        var original = new InvalidOperationException("Scripted begin failure.");
        factory.EnqueueBeginFailure(original);
        factory.Enqueue(defaultIsolation: TransactionIsolation.Serializable);
        factory.Enqueue(defaultIsolation: TransactionIsolation.ReadCommitted);
        Assert.AreSame(original, await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => factory.BeginAsync("Ledger")));
        Assert.IsEmpty(factory.CreatedTransactions);

        await using ILocalTransaction first = await factory.BeginAsync("Ledger",
            new LocalTransactionOptions { Isolation = TransactionIsolation.RepeatableRead });
        await using ILocalTransaction second = await factory.BeginAsync("OtherLedger");
        Assert.AreEqual(TransactionIsolation.RepeatableRead, first.Context.Isolation);
        Assert.AreEqual(TransactionIsolation.ReadCommitted, second.Context.Isolation);
        Assert.AreNotSame(first.Context, second.Context);
    }

    [TestMethod]
    public async Task InvalidOrPrecancelledBeginsDoNotConsumeScriptsAndBeginTokenIsNotLifetimeCancellation()
    {
        var factory = new ScriptedLocalTransactionFactory();
        factory.Enqueue();
        Assert.ThrowsExactly<ArgumentException>(() => factory.BeginAsync("bad alias"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => factory.BeginAsync("Ledger",
            new LocalTransactionOptions { Isolation = (TransactionIsolation)42 }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => factory.BeginAsync("Ledger", cancellation.Token));
        Assert.AreEqual(1, factory.PendingScripts);
        Assert.IsEmpty(factory.CreatedTransactions);

        using var begin = new CancellationTokenSource();
        await using ILocalTransaction transaction = await factory.BeginAsync("Ledger", begin.Token);
        begin.Cancel();
        Assert.AreEqual(TransactionOutcome.Committed, (await transaction.CommitAsync()).Outcome);
    }

    [TestMethod]
    public async Task RollbackOnlyWinsCancellationAndCannotBeOverriddenByACommittedScript()
    {
        var factory = new ScriptedLocalTransactionFactory();
        factory.Enqueue();
        ILocalTransaction transaction = await factory.BeginAsync("Ledger");
        transaction.Context.MarkRollbackOnly("app.first");
        transaction.Context.MarkRollbackOnly("app.second");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        TransactionCompletion completion = await transaction.CommitAsync(cancellation.Token);

        Assert.IsTrue(transaction.Context.IsRollbackOnly);
        Assert.AreEqual("app.first", transaction.Context.RollbackReasonCode);
        Assert.AreEqual(TransactionOutcome.NotCommitted, completion.Outcome);
        Assert.AreEqual(TransactionEndReason.RollbackOnly, completion.EndReason);
        Assert.AreEqual("transaction.rollback_only", completion.Error!.Code);
        Assert.IsFalse(completion.CommitAttempted);
        Assert.IsTrue(completion.RollbackAttempted);
        Assert.IsTrue(completion.RollbackConfirmed);
        Assert.ThrowsExactly<InvalidOperationException>(() => transaction.Context.MarkRollbackOnly("app.late"));
        await transaction.DisposeAsync();
    }

    [TestMethod]
    public async Task PrecancelledCommitReturnsARefusedSnapshotRatherThanThrowing()
    {
        var factory = new ScriptedLocalTransactionFactory();
        factory.Enqueue();
        await using ILocalTransaction transaction = await factory.BeginAsync("Ledger");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        TransactionCompletion result = await transaction.CommitAsync(cancellation.Token);
        Assert.AreEqual(TransactionOutcome.NotCommitted, result.Outcome);
        Assert.AreEqual(TransactionEndReason.Cancellation, result.EndReason);
        Assert.AreEqual("transaction.cancelled_before_commit", result.Error!.Code);
        Assert.IsFalse(result.CommitAttempted);
        Assert.IsTrue(result.RollbackConfirmed);
    }

    [TestMethod]
    public async Task ScriptedUnknownAndCleanupFactsRemainTheSameImmutableCompletion()
    {
        var errors = CleanupErrors();
        var scripted = new TransactionCompletion(TransactionOutcome.Unknown, TransactionEndReason.CommitRequested,
            commitAttempted: true, rollbackAttempted: true, rollbackConfirmed: false,
            new(TransactionErrorPhase.Commit, "transaction.commit_outcome_unknown", "Scripted uncertain commit."),
            errors, durationSeconds: 0.25);
        var factory = new ScriptedLocalTransactionFactory();
        factory.Enqueue(scripted);
        errors.Clear();
        ILocalTransaction transaction = await factory.BeginAsync("Ledger");
        TransactionCompletion result = await transaction.CommitAsync();
        await transaction.DisposeAsync();

        Assert.AreSame(scripted, result);
        Assert.AreSame(result, transaction.Completion);
        Assert.AreEqual(TransactionOutcome.Unknown, result.Outcome);
        Assert.HasCount(3, result.CleanupErrors);
        Assert.AreEqual(0.25, result.DurationSeconds!.Value);
        Assert.ThrowsExactly<InvalidOperationException>(() => transaction.CommitAsync());
        Assert.ThrowsExactly<InvalidOperationException>(() => transaction.RollbackAsync());
        Assert.IsTrue(factory.CreatedTransactions.Single().RollbackRequested);
        Assert.AreEqual(TransactionOutcome.Unknown, transaction.Completion!.Outcome);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RollbackAndUncommittedDisposalNormalizeScriptedCommitFacts(bool explicitRollback)
    {
        var factory = new ScriptedLocalTransactionFactory();
        factory.Enqueue(new(TransactionOutcome.Unknown, TransactionEndReason.CommitRequested,
            commitAttempted: true, rollbackAttempted: true, rollbackConfirmed: false,
            new(TransactionErrorPhase.Commit, "transaction.commit_outcome_unknown", "Scripted uncertain commit."),
            CleanupErrors()));
        ILocalTransaction transaction = await factory.BeginAsync("Ledger");
        if (explicitRollback)
        {
            await transaction.RollbackAsync();
        }
        else
        {
            await transaction.DisposeAsync();
        }
        TransactionCompletion result = transaction.Completion!;
        Assert.AreEqual(TransactionOutcome.NotCommitted, result.Outcome);
        Assert.AreEqual(explicitRollback ? TransactionEndReason.RollbackRequested : TransactionEndReason.UncommittedDisposal,
            result.EndReason);
        Assert.IsNull(result.Error);
        Assert.IsFalse(result.CommitAttempted);
        Assert.IsTrue(result.RollbackAttempted);
        Assert.IsFalse(result.RollbackConfirmed);
        Assert.HasCount(3, result.CleanupErrors);
        ScriptedLocalTransaction recording = factory.CreatedTransactions.Single();
        Assert.IsFalse(recording.CommitRequested);
        Assert.AreEqual(explicitRollback, recording.RollbackRequested);
        await transaction.DisposeAsync();
        Assert.IsTrue(recording.DisposeRequested);
        Assert.AreSame(result, transaction.Completion);
    }

    [TestMethod]
    public async Task ConfirmedCommitCanRetainReleaseFaultsWithoutBecomingBusinessFailure()
    {
        var factory = new ScriptedLocalTransactionFactory();
        factory.Enqueue(new(TransactionOutcome.Committed, TransactionEndReason.CommitRequested,
            commitAttempted: true, rollbackAttempted: false, rollbackConfirmed: false,
            cleanupErrors: CleanupErrors().Skip(1).ToArray()));
        await using ILocalTransaction transaction = await factory.BeginAsync("Ledger");
        TransactionCompletion result = await transaction.CommitAsync();
        Assert.AreEqual(TransactionOutcome.Committed, result.Outcome);
        Assert.HasCount(2, result.CleanupErrors);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public async Task PortableContextRejectsBothNativeOverloadsBeforeWork()
    {
        var factory = new ScriptedLocalTransactionFactory();
        factory.Enqueue();
        await using ILocalTransaction transaction = await factory.BeginAsync("Ledger");
        int calls = 0;
        Assert.IsFalse(transaction.Context is IAdoNetTransactionContext);
        Assert.ThrowsExactly<NotSupportedException>(() => transaction.Context.UseAdoNetAsync((_, _, _) =>
        {
            calls++;
            return Task.CompletedTask;
        }));
        Assert.ThrowsExactly<NotSupportedException>(() => transaction.Context.UseAdoNetAsync<int>((_, _, _) =>
        {
            calls++;
            return Task.FromResult(7);
        }));
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public async Task BothCapacitiesDefaultToSixteenAndExhaustionPreservesQueuedWork()
    {
        var queueBound = new ScriptedLocalTransactionFactory();
        for (int index = 0; index < 16; index++)
        {
            queueBound.Enqueue();
        }
        Assert.ThrowsExactly<InvalidOperationException>(() => queueBound.Enqueue());
        Assert.AreEqual(16, queueBound.PendingScripts);

        var recordedBound = new ScriptedLocalTransactionFactory(scriptCapacity: 1);
        for (int index = 0; index < 16; index++)
        {
            recordedBound.Enqueue();
            await (await recordedBound.BeginAsync("Ledger")).DisposeAsync();
        }
        IReadOnlyList<ScriptedLocalTransaction> retained = recordedBound.CreatedTransactions;
        recordedBound.Enqueue();
        Assert.ThrowsExactly<InvalidOperationException>(() => recordedBound.BeginAsync("Ledger"));
        Assert.AreEqual(1, recordedBound.PendingScripts);
        Assert.HasCount(16, retained);
        Assert.HasCount(16, recordedBound.CreatedTransactions);
        Assert.AreSame(retained[0], recordedBound.CreatedTransactions[0]);
    }

    [TestMethod]
    public void InvalidCapacityOrContradictoryCleanupScriptsFailBeforeAdmission()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ScriptedLocalTransactionFactory(scriptCapacity: 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ScriptedLocalTransactionFactory(recordingCapacity: 0));
        var factory = new ScriptedLocalTransactionFactory();
        TransactionError[] errors = CleanupErrors().ToArray();
        Assert.ThrowsExactly<ArgumentException>(() => factory.Enqueue(new(TransactionOutcome.Unknown,
            TransactionEndReason.CommitRequested, true, true, false, cleanupErrors: [errors[1], errors[0]])));
        Assert.ThrowsExactly<ArgumentException>(() => factory.Enqueue(new(TransactionOutcome.NotCommitted,
            TransactionEndReason.UncommittedDisposal, false, true, true)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => factory.Enqueue(defaultIsolation: (TransactionIsolation)99));
        Assert.AreEqual(0, factory.PendingScripts);
        Assert.IsEmpty(factory.CreatedTransactions);
    }

    private static List<TransactionError> CleanupErrors()
        => [new(TransactionErrorPhase.Rollback, "transaction.rollback_failed", "Scripted rollback failure."),
            new(TransactionErrorPhase.TransactionDispose, "transaction.transaction_disposal_failed", "Scripted transaction disposal failure."),
            new(TransactionErrorPhase.ConnectionDispose, "transaction.connection_disposal_failed", "Scripted connection disposal failure.")];

    private sealed class PortableStore
    {
        internal ILocalTransactionContext? Received
        {
            get; private set;
        }
        internal Task WriteAsync(ILocalTransactionContext context)
        {
            Received = context;
            return Task.CompletedTask;
        }
    }
}

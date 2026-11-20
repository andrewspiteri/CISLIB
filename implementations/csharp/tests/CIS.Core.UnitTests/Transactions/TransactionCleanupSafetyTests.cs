using System.Text.Json;
using CIS.TestSupport.Transactions;
using CIS.Transactions;
using CIS.Transactions.Hosting;
using CIS.Transactions.AdoNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Transactions;

[TestClass]
public sealed class TransactionCleanupSafetyTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task IndependentRollbackBudgetRequestsCancellationButCannotAbandonNativeRollback()
    {
        var entered = Signal();
        var budgetExpired = Signal();
        var release = Signal();
        bool initiallyCanceled = true;
        using var callerCancellation = new CancellationTokenSource();
        callerCancellation.Cancel();
        using var source = new RecordingTransactionDataSource
        {
            ConnectionCreated = connection => connection.TransactionCreated = native => native.RollbackHandler = async token =>
            {
                initiallyCanceled = token.IsCancellationRequested;
                using CancellationTokenRegistration registration = token.Register(() => budgetExpired.TrySetResult());
                entered.TrySetResult();
                await release.Task;
                token.ThrowIfCancellationRequested();
            }
        };
        using ServiceProvider provider = Provider(source, rollbackTimeout: TimeSpan.FromSeconds(1));
        ILocalTransaction owner = await provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");
        Task<TransactionCompletion> completing = owner.CommitAsync(callerCancellation.Token);
        try
        {
            await entered.Task.WaitAsync(Deadline);
            RecordingNativeTransaction native = source.Connections.Single().Transactions.Single();
            Assert.IsFalse(initiallyCanceled, "The request token cannot pre-cancel the fresh rollback budget.");
            Assert.AreNotEqual(callerCancellation.Token, native.RollbackToken);
            Assert.AreEqual(0, native.CommitCalls);
            await budgetExpired.Task.WaitAsync(Deadline);
            Assert.IsTrue(native.RollbackToken.IsCancellationRequested);
            Assert.IsFalse(completing.IsCompleted,
                "Budget expiry requests native cancellation; the provider still owns its actual in-flight rollback.");
            Assert.AreEqual(LocalTransactionStatus.Completing, owner.Status);
            Assert.IsNull(owner.Completion);
            Assert.AreEqual(0, native.DisposeCalls);
            Assert.AreEqual(0, source.Connections.Single().DisposeCalls);

            release.TrySetResult();
            TransactionCompletion result = await completing.WaitAsync(Deadline);
            Assert.AreEqual(TransactionOutcome.NotCommitted, result.Outcome);
            Assert.AreEqual(TransactionEndReason.Cancellation, result.EndReason);
            Assert.AreEqual("transaction.cancelled_before_commit", result.Error?.Code);
            Assert.IsTrue(result.RollbackAttempted);
            Assert.IsFalse(result.RollbackConfirmed);
            Assert.AreEqual("transaction.rollback_failed", result.CleanupErrors.Single().Code);
        }
        finally
        {
            release.TrySetResult();
            try
            {
                await completing;
            }
            finally
            {
                await owner.DisposeAsync();
            }
        }
        Assert.AreEqual(1, source.Connections.Single().Transactions.Single().RollbackCalls);
        Assert.AreEqual(1, source.Connections.Single().Transactions.Single().DisposeCalls);
        Assert.AreEqual(1, source.Connections.Single().DisposeCalls);
        Assert.AreEqual(0, source.DisposeCalls);
    }

    [TestMethod]
    public async Task FailedRollbackAndBothDisposalsAreAttemptedOnceAndRetainedInActualOrder()
    {
        using RecordingTransactionDataSource source = FailingCleanupSource();
        using ServiceProvider provider = Provider(source);
        ILocalTransaction owner = await provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");
        TransactionCompletion completion;
        try
        {
            completion = await owner.RollbackAsync();
            Assert.AreEqual(TransactionOutcome.NotCommitted, completion.Outcome);
            Assert.AreEqual(TransactionEndReason.RollbackRequested, completion.EndReason);
            Assert.IsNull(completion.Error, "Rollback-request operational failures belong only in cleanup diagnostics.");
            Assert.IsFalse(completion.CommitAttempted);
            Assert.IsTrue(completion.RollbackAttempted);
            Assert.IsFalse(completion.RollbackConfirmed);
            CollectionAssert.AreEqual(new[]
            {
                TransactionErrorPhase.Rollback,
                TransactionErrorPhase.TransactionDispose,
                TransactionErrorPhase.ConnectionDispose
            }, completion.CleanupErrors.Select(error => error.Phase).ToArray());
            CollectionAssert.AreEqual(new[]
            {
                "transaction.rollback_failed",
                "transaction.transaction_disposal_failed",
                "transaction.connection_disposal_failed"
            }, completion.CleanupErrors.Select(error => error.Code).ToArray());
            string serialized = JsonSerializer.Serialize(completion);
            Assert.IsFalse(serialized.Contains("fixture-secret", StringComparison.Ordinal));
            Assert.IsFalse(serialized.Contains("SELECT", StringComparison.Ordinal));
            Assert.AreSame(completion, owner.Completion);
            Assert.AreEqual(LocalTransactionStatus.Completed, owner.Status);
            Assert.ThrowsExactly<InvalidOperationException>(() => owner.CommitAsync());
            Assert.ThrowsExactly<InvalidOperationException>(() => owner.RollbackAsync());
        }
        finally
        {
            await owner.DisposeAsync();
            await owner.DisposeAsync();
        }
        Assert.AreSame(completion, owner.Completion);
        AssertCleanupCounts(source);
        CollectionAssert.AreEqual(new[] { "open", "begin", "rollback", "transaction.dispose", "connection.dispose" },
            source.Events.ToArray());
    }

    [TestMethod]
    public async Task AwaitUsingCleanupCannotReplaceAnEscapingApplicationCallbackException()
    {
        using RecordingTransactionDataSource source = FailingCleanupSource();
        using ServiceProvider provider = Provider(source);
        ILocalTransaction? retained = null;
        var original = new InvalidOperationException("Original application fixture failure.");

        async Task ApplicationOperation()
        {
            await using ILocalTransaction owner = await provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");
            retained = owner;
            await owner.Context.UseAdoNetAsync((_, _, _) => Task.FromException(original));
        }

        InvalidOperationException observed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(ApplicationOperation);
        Assert.AreSame(original, observed);
        Assert.IsNotNull(retained);
        Assert.AreEqual("transaction.work_failed", retained.Context.RollbackReasonCode);
        Assert.AreEqual(TransactionOutcome.NotCommitted, retained.Completion?.Outcome);
        Assert.AreEqual(TransactionEndReason.UncommittedDisposal, retained.Completion?.EndReason);
        Assert.AreEqual(3, retained.Completion?.CleanupErrors.Count);
        AssertCleanupCounts(source);
    }

    [TestMethod]
    public async Task CancellationAfterNativeBeginKeepsItsOriginalTokenDespiteAllCleanupFailures()
    {
        using var cancellation = new CancellationTokenSource();
        using RecordingTransactionDataSource source = FailingCleanupSource();
        Action<RecordingTransactionConnection>? configure = source.ConnectionCreated;
        source.ConnectionCreated = connection =>
        {
            configure!(connection);
            Action<RecordingNativeTransaction>? transactionCreated = connection.TransactionCreated;
            connection.TransactionCreated = transaction =>
            {
                transactionCreated!(transaction);
                cancellation.Cancel();
            };
        };
        using ServiceProvider provider = Provider(source);
        OperationCanceledException observed = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger", cancellation.Token));
        Assert.AreEqual(cancellation.Token, observed.CancellationToken);
        AssertCleanupCounts(source);
        CollectionAssert.AreEqual(new[] { "open", "begin", "rollback", "transaction.dispose", "connection.dispose" },
            source.Events.ToArray());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnavailableStartOrEndMeasurementLeavesDurationUnknownWithoutChangingNativeCompletion(bool failAtStart)
    {
        using var source = new RecordingTransactionDataSource();
        using ServiceProvider provider = Provider(source, clock: new FailingTimestampClock(failAtStart));
        await using ILocalTransaction owner = await provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");
        TransactionCompletion result = await owner.CommitAsync();
        Assert.AreEqual(TransactionOutcome.Committed, result.Outcome);
        Assert.IsNull(result.DurationSeconds);
        Assert.IsNull(result.Error);
        Assert.AreEqual(0, result.CleanupErrors.Count);
        Assert.AreEqual(1, source.Connections.Single().Transactions.Single().CommitCalls);
        Assert.AreEqual(0, source.Connections.Single().Transactions.Single().RollbackCalls);
        Assert.AreEqual(1, source.Connections.Single().Transactions.Single().DisposeCalls);
        Assert.AreEqual(1, source.Connections.Single().DisposeCalls);
        Assert.AreEqual(0, source.DisposeCalls);
    }

    private static RecordingTransactionDataSource FailingCleanupSource() => new()
    {
        ConnectionCreated = connection =>
        {
            connection.DisposeHandler = () => ValueTask.FromException(new IOException("fixture-secret connection SELECT release"));
            connection.TransactionCreated = transaction =>
            {
                transaction.RollbackHandler = _ => Task.FromException(new IOException("fixture-secret rollback SELECT release"));
                transaction.DisposeHandler = () => ValueTask.FromException(new IOException("fixture-secret transaction SELECT release"));
            };
        }
    };

    private static void AssertCleanupCounts(RecordingTransactionDataSource source)
    {
        RecordingTransactionConnection connection = source.Connections.Single();
        RecordingNativeTransaction transaction = connection.Transactions.Single();
        Assert.AreEqual(0, transaction.CommitCalls);
        Assert.AreEqual(1, transaction.RollbackCalls);
        Assert.AreEqual(1, transaction.DisposeCalls);
        Assert.AreEqual(1, connection.DisposeCalls);
        Assert.AreEqual(0, source.DisposeCalls);
    }

    private static ServiceProvider Provider(RecordingTransactionDataSource source, TimeSpan? rollbackTimeout = null,
        TimeProvider? clock = null)
    {
        var services = new ServiceCollection();
        if (clock is not null)
        {
            services.AddSingleton(clock);
        }
        services.AddCisTransactions(new()
        {
            RollbackTimeout = rollbackTimeout ?? TimeSpan.FromSeconds(30)
        });
        services.AddCisAdoNetDatabase("Ledger", _ => source, new()
        {
            SupportedIsolationLevels = [TransactionIsolation.ReadCommitted]
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class FailingTimestampClock(bool failAtStart) : TimeProvider
    {
        private int calls;
        public override long GetTimestamp()
        {
            if (Interlocked.Increment(ref calls) > 1 || failAtStart)
            {
                throw new InvalidOperationException("The fixture clock cannot provide this measurement.");
            }
            return base.GetTimestamp();
        }
    }
}

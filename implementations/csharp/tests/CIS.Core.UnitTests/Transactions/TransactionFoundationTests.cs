using System.Data;
using System.Data.Common;
using CIS.TestSupport.Transactions;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using CIS.Transactions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Transactions;

[TestClass]
public sealed class TransactionFoundationTests
{
    [TestMethod]
    public async Task SequentialParticipantsBorrowExactHandlesAndOwnersReleaseIndependentLeases()
    {
        using var source = new RecordingTransactionDataSource();
        using ServiceProvider provider = Provider(source);
        ILocalTransactionFactory factory = provider.GetRequiredService<ILocalTransactionFactory>();
        await using ILocalTransaction first = await factory.BeginAsync("Ledger",
            new LocalTransactionOptions { Isolation = TransactionIsolation.RepeatableRead });
        ILocalTransaction second = await factory.BeginAsync("Ledger");
        try
        {
            Assert.IsFalse(first.Context is ILocalTransaction);
            Assert.IsFalse(first.Context is IAsyncDisposable);
            Assert.AreEqual(LocalTransactionStatus.Active, first.Status);
            Assert.IsNull(first.Completion);
            RecordingTransactionConnection[] connections = source.Connections.ToArray();
            Assert.AreEqual(2, connections.Length);
            Assert.AreNotSame(connections[0], connections[1]);
            for (int index = 0; index < 2; index++)
            {
                bool value = await first.Context.UseAdoNetAsync((connection, transaction, _) =>
                {
                    Assert.AreSame(connections[0], connection);
                    Assert.AreSame(connections[0].Transactions.Single(), transaction);
                    return Task.FromResult(false);
                });
                Assert.IsFalse(value, "An ordinary business result does not control transaction completion.");
            }
            TransactionCompletion committed = await first.CommitAsync();
            Assert.AreEqual(TransactionOutcome.Committed, committed.Outcome);
            Assert.IsFalse(committed.RollbackAttempted);
            Assert.AreSame(committed, first.Completion);
            Assert.AreEqual(1, connections[0].Transactions.Single().CommitCalls);
            Assert.AreEqual(1, connections[0].Transactions.Single().DisposeCalls);
            Assert.AreEqual(1, connections[0].DisposeCalls);
            await second.DisposeAsync();
            Assert.AreEqual(TransactionOutcome.NotCommitted, second.Completion?.Outcome);
            Assert.AreEqual(TransactionEndReason.UncommittedDisposal, second.Completion?.EndReason);
            Assert.AreEqual(1, connections[1].Transactions.Single().RollbackCalls);
            Assert.AreEqual(1, connections[1].DisposeCalls);
            Assert.AreEqual(0, source.DisposeCalls);
            Assert.ThrowsExactly<InvalidOperationException>(() => first.CommitAsync());
            Assert.ThrowsExactly<InvalidOperationException>(() => first.Context.MarkRollbackOnly("too_late"));
            Assert.ThrowsExactly<InvalidOperationException>(() => first.Context.UseAdoNetAsync((_, _, _) => Task.CompletedTask));
        }
        finally
        {
            await second.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task OpenFailurePreservesTheOriginalAndReturnsNoOwner()
    {
        var original = new InvalidOperationException("private provider open fixture fault");
        using var source = new RecordingTransactionDataSource
        {
            OpenHandler = _ => ValueTask.FromException<DbConnection>(original)
        };
        using ServiceProvider provider = Provider(source);
        InvalidOperationException observed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger"));
        Assert.AreSame(original, observed);
        Assert.AreEqual(1, source.OpenCalls);
        Assert.AreEqual(0, source.Connections.Count);
        Assert.AreEqual(0, source.DisposeCalls);
    }

    [TestMethod]
    public async Task FailedBeginAttemptsConnectionReleaseWithoutMaskingItsOriginalFault()
    {
        var original = new InvalidOperationException("private provider begin fixture fault");
        using var source = new RecordingTransactionDataSource
        {
            ConnectionCreated = connection =>
            {
                connection.BeginHandler = (_, _) => ValueTask.FromException<DbTransaction>(original);
                connection.DisposeHandler = () => ValueTask.FromException(new IOException("private release fixture fault"));
            }
        };
        using ServiceProvider provider = Provider(source);
        InvalidOperationException observed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger"));
        Assert.AreSame(original, observed);
        Assert.AreEqual(1, source.Connections.Single().DisposeCalls);
        CollectionAssert.AreEqual(new[] { "open", "begin", "connection.dispose" }, source.Events.ToArray());
        Assert.AreEqual(0, source.DisposeCalls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancellationAfterAcquisitionReleasesOnlyAcquiredResourcesWithIndependentRollback(bool afterNativeBegin)
    {
        using var cancellation = new CancellationTokenSource();
        using var source = new RecordingTransactionDataSource
        {
            ConnectionCreated = connection =>
            {
                if (afterNativeBegin)
                {
                    connection.TransactionCreated = _ => cancellation.Cancel();
                }
                else
                {
                    cancellation.Cancel();
                }
            }
        };
        using ServiceProvider provider = Provider(source);
        OperationCanceledException observed = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger", cancellation.Token));
        Assert.AreEqual(cancellation.Token, observed.CancellationToken);
        RecordingTransactionConnection connection = source.Connections.Single();
        Assert.AreEqual(1, connection.DisposeCalls);
        Assert.AreEqual(afterNativeBegin ? 1 : 0, connection.BeginCalls);
        if (afterNativeBegin)
        {
            RecordingNativeTransaction native = connection.Transactions.Single();
            Assert.AreEqual(1, native.RollbackCalls);
            Assert.IsTrue(native.RollbackToken.CanBeCanceled);
            Assert.IsFalse(native.RollbackToken.IsCancellationRequested);
            Assert.AreNotEqual(cancellation.Token, native.RollbackToken);
            Assert.AreEqual(1, native.DisposeCalls);
        }
        Assert.AreEqual(0, source.DisposeCalls);
    }

    [TestMethod]
    [DataRow(TransactionIsolation.ReadCommitted, IsolationLevel.ReadCommitted)]
    [DataRow(TransactionIsolation.RepeatableRead, IsolationLevel.RepeatableRead)]
    [DataRow(TransactionIsolation.Serializable, IsolationLevel.Serializable)]
    public async Task DeclaredIsolationMapsExactlyWithoutFallback(TransactionIsolation requested, IsolationLevel native)
    {
        using var source = new RecordingTransactionDataSource();
        using ServiceProvider provider = Provider(source);
        await using ILocalTransaction owner = await provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger",
            new LocalTransactionOptions { Isolation = requested });
        Assert.AreEqual(requested, owner.Context.Isolation);
        Assert.AreEqual(native, source.Connections.Single().RequestedIsolation);
        Assert.AreEqual(1, source.Connections.Single().BeginCalls);
    }

    [TestMethod]
    public async Task AnEscapingCallbackFaultPoisonsTheUnitBeforeTheCallerCatchesIt()
    {
        using var source = new RecordingTransactionDataSource();
        using ServiceProvider provider = Provider(source);
        await using ILocalTransaction owner = await provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");
        var original = new InvalidOperationException("application fixture fault");
        InvalidOperationException observed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            owner.Context.UseAdoNetAsync((_, _, _) => Task.FromException(original)));
        Assert.AreSame(original, observed);
        Assert.IsTrue(owner.Context.IsRollbackOnly);
        Assert.AreEqual("transaction.work_failed", owner.Context.RollbackReasonCode);
        owner.Context.MarkRollbackOnly("later_reason");
        Assert.AreEqual("transaction.work_failed", owner.Context.RollbackReasonCode);
        TransactionCompletion result = await owner.CommitAsync();
        Assert.AreEqual(TransactionOutcome.NotCommitted, result.Outcome);
        Assert.AreEqual(TransactionEndReason.RollbackOnly, result.EndReason);
        Assert.AreEqual("transaction.rollback_only", result.Error?.Code);
        Assert.AreEqual(0, source.Connections.Single().Transactions.Single().CommitCalls);
        Assert.IsTrue(result.RollbackConfirmed);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AdmittedCancellationPoisonsBeforeOrAfterTheActualCallback(bool afterWork)
    {
        using var source = new RecordingTransactionDataSource();
        using ServiceProvider provider = Provider(source);
        await using ILocalTransaction owner = await provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");
        using var cancellation = new CancellationTokenSource();
        bool entered = false;
        if (!afterWork)
        {
            cancellation.Cancel();
        }
        OperationCanceledException observed = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            owner.Context.UseAdoNetAsync((_, _, token) =>
            {
                entered = true;
                Assert.AreEqual(cancellation.Token, token);
                cancellation.Cancel();
                return Task.CompletedTask;
            }, cancellation.Token));
        Assert.AreEqual(cancellation.Token, observed.CancellationToken);
        Assert.AreEqual(afterWork, entered);
        Assert.AreEqual("transaction.work_cancelled", owner.Context.RollbackReasonCode);
        TransactionCompletion result = await owner.CommitAsync();
        Assert.IsFalse(result.CommitAttempted);
        Assert.AreEqual(TransactionOutcome.NotCommitted, result.Outcome);
    }

    [TestMethod]
    public async Task FailedNativeCommitStaysUnknownEvenAfterConfirmedRollback()
    {
        using var source = new RecordingTransactionDataSource
        {
            ConnectionCreated = connection => connection.TransactionCreated = native =>
                native.CommitHandler = _ => throw new IOException("private lost acknowledgement fixture")
        };
        using ServiceProvider provider = Provider(source);
        await using ILocalTransaction owner = await provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");
        TransactionCompletion result = await owner.CommitAsync();
        Assert.AreEqual(TransactionOutcome.Unknown, result.Outcome);
        Assert.AreEqual(TransactionEndReason.CommitRequested, result.EndReason);
        Assert.IsTrue(result.CommitAttempted);
        Assert.IsTrue(result.RollbackConfirmed);
        Assert.AreEqual("transaction.commit_outcome_unknown", result.Error?.Code);
        Assert.AreEqual(1, source.Connections.Single().Transactions.Single().CommitCalls);
        CollectionAssert.AreEqual(new[] { "open", "begin", "commit", "rollback", "transaction.dispose", "connection.dispose" },
            source.Events.ToArray());
    }

    [TestMethod]
    public async Task ConfirmedCommitKeepsItsPersistenceOutcomeWhenBothNativeDisposalsFail()
    {
        using var source = new RecordingTransactionDataSource
        {
            ConnectionCreated = connection =>
            {
                connection.DisposeHandler = () => ValueTask.FromException(new IOException("private connection fault"));
                connection.TransactionCreated = native => native.DisposeHandler = () =>
                    ValueTask.FromException(new IOException("private transaction fault"));
            }
        };
        using ServiceProvider provider = Provider(source);
        ILocalTransaction owner = await provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");
        try
        {
            TransactionCompletion result = await owner.CommitAsync();
            Assert.AreEqual(TransactionOutcome.Committed, result.Outcome);
            Assert.IsNull(result.Error);
            CollectionAssert.AreEqual(new[] { TransactionErrorPhase.TransactionDispose, TransactionErrorPhase.ConnectionDispose },
                result.CleanupErrors.Select(error => error.Phase).ToArray());
            Assert.IsFalse(result.RollbackAttempted);
            Assert.AreEqual(LocalTransactionStatus.Completed, owner.Status);
            Assert.IsTrue(result.DurationSeconds >= 0);
        }
        finally
        {
            await owner.DisposeAsync();
        }
        Assert.AreEqual(1, source.Connections.Single().DisposeCalls);
        Assert.AreEqual(0, source.DisposeCalls);
    }

    internal static ServiceProvider Provider(RecordingTransactionDataSource source)
    {
        var services = new ServiceCollection();
        services.AddCisAdoNetDatabase("Ledger", _ => source, new()
        {
            SupportedIsolationLevels = [TransactionIsolation.ReadCommitted, TransactionIsolation.RepeatableRead, TransactionIsolation.Serializable]
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}

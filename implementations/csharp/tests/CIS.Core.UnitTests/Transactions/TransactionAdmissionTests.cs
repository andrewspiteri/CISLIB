using CIS.TestSupport.Transactions;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Transactions;

[TestClass]
public sealed class TransactionAdmissionTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task OverlapRecursionAndCompletionFromACallbackRejectWithoutExecutingRejectedWork()
    {
        using var source = new RecordingTransactionDataSource();
        using ServiceProvider provider = TransactionFoundationTests.Provider(source);
        await using ILocalTransaction owner = await provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");
        var entered = Signal();
        var release = Signal();
        int rejectedCallbacks = 0;
        Task work = owner.Context.UseAdoNetAsync(async (_, _, _) =>
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => owner.Context.UseAdoNetAsync((_, _, _) =>
            {
                rejectedCallbacks++;
                return Task.CompletedTask;
            }));
            Assert.ThrowsExactly<InvalidOperationException>(() => owner.CommitAsync());
            Assert.ThrowsExactly<InvalidOperationException>(() => owner.RollbackAsync());
            Assert.ThrowsExactly<InvalidOperationException>(() => owner.DisposeAsync());
            entered.TrySetResult();
            await release.Task;
        });
        try
        {
            await entered.Task.WaitAsync(Deadline);
            Assert.ThrowsExactly<InvalidOperationException>(() => owner.Context.UseAdoNetAsync((_, _, _) =>
            {
                rejectedCallbacks++;
                return Task.CompletedTask;
            }));
            Assert.ThrowsExactly<InvalidOperationException>(() => owner.CommitAsync());
            Assert.ThrowsExactly<InvalidOperationException>(() => owner.RollbackAsync());
            Assert.AreEqual(0, rejectedCallbacks);
            Assert.AreEqual(LocalTransactionStatus.Active, owner.Status);
            Assert.IsNull(owner.Completion);
            Assert.AreEqual(0, source.Connections.Single().Transactions.Single().CommitCalls);
            Assert.AreEqual(0, source.Connections.Single().Transactions.Single().RollbackCalls);
        }
        finally
        {
            release.TrySetResult();
            await work;
        }
        bool nextEntered = false;
        await owner.Context.UseAdoNetAsync((_, _, _) =>
        {
            nextEntered = true;
            return Task.CompletedTask;
        });
        Assert.IsTrue(nextEntered, "Rejected work must not consume later valid callback admission.");
        Assert.IsFalse(owner.Context.IsRollbackOnly, "Caught rejected misuse is not an admitted callback failure.");
        Assert.AreEqual(TransactionOutcome.Committed, (await owner.CommitAsync()).Outcome);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExternalDisposalClosesAdmissionAndAcceptsTheAdmittedCallbacksFinalMark(bool callbackFaults)
    {
        using var source = new RecordingTransactionDataSource();
        using ServiceProvider provider = TransactionFoundationTests.Provider(source);
        ILocalTransaction owner = await provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");
        var entered = Signal();
        var release = Signal();
        var original = new InvalidOperationException("Admitted callback fixture fault.");
        Task work = owner.Context.UseAdoNetAsync(async (_, _, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            owner.Context.MarkRollbackOnly("participant.final_mark");
            if (callbackFaults)
            {
                throw original;
            }
        });
        Task? disposal = null;
        try
        {
            await entered.Task.WaitAsync(Deadline);
            disposal = owner.DisposeAsync().AsTask();
            Task repeated = owner.DisposeAsync().AsTask();
            Assert.AreSame(disposal, repeated);
            Assert.IsFalse(disposal.IsCompleted);
            Assert.AreEqual(LocalTransactionStatus.Completing, owner.Status);
            Assert.IsNull(owner.Completion);
            Assert.ThrowsExactly<InvalidOperationException>(() => owner.Context.MarkRollbackOnly("outside.too_late"));
            Assert.ThrowsExactly<InvalidOperationException>(() => owner.Context.UseAdoNetAsync((_, _, _) => Task.CompletedTask));
            RecordingNativeTransaction native = source.Connections.Single().Transactions.Single();
            Assert.AreEqual(0, native.RollbackCalls);
            Assert.AreEqual(0, native.DisposeCalls);

            release.TrySetResult();
            if (callbackFaults)
            {
                InvalidOperationException observed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => work);
                Assert.AreSame(original, observed);
            }
            else
            {
                await work;
            }
            await disposal.WaitAsync(Deadline);
            Assert.AreEqual("participant.final_mark", owner.Context.RollbackReasonCode);
            Assert.AreEqual(TransactionOutcome.NotCommitted, owner.Completion?.Outcome);
            Assert.AreEqual(TransactionEndReason.UncommittedDisposal, owner.Completion?.EndReason);
            Assert.AreEqual(1, native.RollbackCalls);
            Assert.AreEqual(1, native.DisposeCalls);
            Assert.ThrowsExactly<InvalidOperationException>(() => owner.Context.MarkRollbackOnly("completed"));
        }
        finally
        {
            release.TrySetResult();
            try
            {
                try
                {
                    await work;
                }
                catch (InvalidOperationException error) when (callbackFaults && ReferenceEquals(error, original))
                {
                }
            }
            finally
            {
                await (disposal ?? owner.DisposeAsync().AsTask());
            }
        }
        await owner.DisposeAsync();
        Assert.AreEqual(1, source.Connections.Single().DisposeCalls);
        Assert.AreEqual(0, source.DisposeCalls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DisposalJoinsAnAlreadyInvokedNativeCommitWithoutIssuingAnotherCompletion(bool commitFails)
    {
        var entered = Signal();
        var release = Signal();
        using var source = new RecordingTransactionDataSource
        {
            ConnectionCreated = connection => connection.TransactionCreated = native => native.CommitHandler = async _ =>
            {
                entered.TrySetResult();
                await release.Task;
                if (commitFails)
                {
                    throw new IOException("Lost acknowledgement fixture.");
                }
            }
        };
        using ServiceProvider provider = TransactionFoundationTests.Provider(source);
        ILocalTransaction owner = await provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");
        Task<TransactionCompletion> committing = owner.CommitAsync();
        Task? disposing = null;
        try
        {
            await entered.Task.WaitAsync(Deadline);
            disposing = owner.DisposeAsync().AsTask();
            Assert.AreSame(committing, disposing);
            Assert.IsFalse(disposing.IsCompleted);
            Assert.IsNull(owner.Completion);
            Assert.ThrowsExactly<InvalidOperationException>(() => owner.CommitAsync());
            Assert.ThrowsExactly<InvalidOperationException>(() => owner.RollbackAsync());
            Assert.ThrowsExactly<InvalidOperationException>(() => owner.Context.MarkRollbackOnly("too_late"));
            Assert.ThrowsExactly<InvalidOperationException>(() => owner.Context.UseAdoNetAsync((_, _, _) => Task.CompletedTask));
            release.TrySetResult();
            TransactionCompletion result = await committing.WaitAsync(Deadline);
            await disposing;
            Assert.AreEqual(commitFails ? TransactionOutcome.Unknown : TransactionOutcome.Committed, result.Outcome);
            Assert.AreSame(result, owner.Completion);
        }
        finally
        {
            release.TrySetResult();
            try
            {
                await committing;
            }
            finally
            {
                await (disposing ?? owner.DisposeAsync().AsTask());
            }
        }
        RecordingNativeTransaction transaction = source.Connections.Single().Transactions.Single();
        Assert.AreEqual(1, transaction.CommitCalls);
        Assert.AreEqual(commitFails ? 1 : 0, transaction.RollbackCalls);
        Assert.AreEqual(1, transaction.DisposeCalls);
        Assert.AreEqual(1, source.Connections.Single().DisposeCalls);
    }

    [TestMethod]
    public async Task CompletionAndDisposalRemainPendingUntilNativeResourceDisposalActuallySettles()
    {
        var disposing = Signal();
        var release = Signal();
        using var source = new RecordingTransactionDataSource
        {
            ConnectionCreated = connection => connection.TransactionCreated = native => native.DisposeHandler = async () =>
            {
                disposing.TrySetResult();
                await release.Task;
            }
        };
        using ServiceProvider provider = TransactionFoundationTests.Provider(source);
        ILocalTransaction owner = await provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");
        Task<TransactionCompletion> committing = owner.CommitAsync();
        Task? joining = null;
        try
        {
            await disposing.Task.WaitAsync(Deadline);
            joining = owner.DisposeAsync().AsTask();
            Assert.IsFalse(committing.IsCompleted);
            Assert.IsFalse(joining.IsCompleted);
            Assert.AreEqual(LocalTransactionStatus.Completing, owner.Status);
            Assert.IsNull(owner.Completion);
            Assert.AreEqual(0, source.Connections.Single().DisposeCalls,
                "Connection release must follow the actual native transaction-disposal settlement.");
        }
        finally
        {
            release.TrySetResult();
            try
            {
                await committing;
            }
            finally
            {
                await (joining ?? owner.DisposeAsync().AsTask());
            }
        }
        Assert.AreEqual(TransactionOutcome.Committed, owner.Completion?.Outcome);
        Assert.AreEqual(1, source.Connections.Single().Transactions.Single().DisposeCalls);
        Assert.AreEqual(1, source.Connections.Single().DisposeCalls);
    }

    [TestMethod]
    public async Task CancellationCannotAbandonAnAdmittedCallbackThatStillOwnsNativeWork()
    {
        var entered = Signal();
        var release = Signal();
        var settled = Signal();
        using var cancellation = new CancellationTokenSource();
        using var source = new RecordingTransactionDataSource();
        using ServiceProvider provider = TransactionFoundationTests.Provider(source);
        ILocalTransaction owner = await provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");
        Task work = owner.Context.UseAdoNetAsync(async (_, _, token) =>
        {
            Assert.AreEqual(cancellation.Token, token);
            entered.TrySetResult();
            try
            {
                await release.Task;
            }
            finally
            {
                settled.TrySetResult();
            }
        }, cancellation.Token);
        Task? disposing = null;
        try
        {
            await entered.Task.WaitAsync(Deadline);
            cancellation.Cancel();
            disposing = owner.DisposeAsync().AsTask();
            Assert.IsFalse(work.IsCompleted);
            Assert.IsFalse(disposing.IsCompleted);
            Assert.IsFalse(settled.Task.IsCompleted);
            Assert.AreEqual(0, source.Connections.Single().Transactions.Single().RollbackCalls);
            release.TrySetResult();
            OperationCanceledException observed = await Assert.ThrowsAsync<OperationCanceledException>(() => work);
            Assert.AreEqual(cancellation.Token, observed.CancellationToken);
            await disposing.WaitAsync(Deadline);
            Assert.IsTrue(settled.Task.IsCompletedSuccessfully);
            Assert.AreEqual("transaction.work_cancelled", owner.Context.RollbackReasonCode);
        }
        finally
        {
            release.TrySetResult();
            try
            {
                try
                {
                    await work;
                }
                catch (OperationCanceledException error) when (error.CancellationToken == cancellation.Token)
                {
                }
            }
            finally
            {
                await (disposing ?? owner.DisposeAsync().AsTask());
            }
        }
        Assert.AreEqual(TransactionOutcome.NotCommitted, owner.Completion?.Outcome);
        Assert.AreEqual(1, source.Connections.Single().Transactions.Single().RollbackCalls);
        Assert.AreEqual(1, source.Connections.Single().DisposeCalls);
    }

    [TestMethod]
    public async Task ConcurrentMarkAndCompletionNeverCommitAfterAnAcceptedMark()
    {
        using var source = new RecordingTransactionDataSource();
        using ServiceProvider provider = TransactionFoundationTests.Provider(source);
        ILocalTransactionFactory factory = provider.GetRequiredService<ILocalTransactionFactory>();
        for (int iteration = 0; iteration < 4; iteration++)
        {
            await using ILocalTransaction owner = await factory.BeginAsync("Ledger");
            RecordingNativeTransaction native = source.Connections.Last().Transactions.Single();
            var start = Signal();
            Task<bool> marking = Task.Run(async () =>
            {
                await start.Task;
                try
                {
                    owner.Context.MarkRollbackOnly("accepted.concurrent_mark");
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            });
            Task<TransactionCompletion> committing = Task.Run(async () =>
            {
                await start.Task;
                return await owner.CommitAsync();
            });
            try
            {
                start.TrySetResult();
                bool accepted = await marking.WaitAsync(Deadline);
                TransactionCompletion result = await committing.WaitAsync(Deadline);
                Assert.AreEqual(accepted ? TransactionOutcome.NotCommitted : TransactionOutcome.Committed, result.Outcome);
                Assert.AreEqual(accepted ? 0 : 1, native.CommitCalls,
                    "Mark and completion must share the same ownership claim, without a lost accepted mark.");
                Assert.AreEqual(accepted ? "accepted.concurrent_mark" : null, owner.Context.RollbackReasonCode);
            }
            finally
            {
                start.TrySetResult();
                try
                {
                    await marking;
                }
                finally
                {
                    await committing;
                }
            }
        }
        Assert.AreEqual(0, source.DisposeCalls);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

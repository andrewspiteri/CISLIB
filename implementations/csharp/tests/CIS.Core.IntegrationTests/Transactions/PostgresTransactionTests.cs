using System.Data.Common;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Transactions;

[TestClass]
[DoNotParallelize]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
public sealed class PostgresTransactionTests
{
    [TestMethod]
    public async Task ThreeParticipantsShareExactNativeHandlesAndCommitBecomesVisibleTogether()
    {
        await using PostgresTransactionFixture fixture = await PostgresTransactionFixture.CreateAsync();
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("AcceptanceDatabase");
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        var witness = new NativePairWitness();
        PostgresAcceptanceConsumer consumer = fixture.Consumer(witness.Observe);

        PostgresBusinessResult result = await consumer.ExecuteAsync(owner.Context, Guid.NewGuid(), accepted: true);

        Assert.IsTrue(result.Success);
        CollectionAssert.AreEqual(new[] { "business", "result", "outgoing" }, witness.Participants.ToArray());
        Assert.AreNotEqual(witness.BackendPid, observer.ProcessID, "Visibility is observed through a separate live server session.");
        Assert.AreEqual(new PostgresTableCounts(0, 0, 0), await fixture.CountsAsync(observer));

        TransactionCompletion completion = await owner.CommitAsync();

        AssertCommitted(completion);
        Assert.AreEqual(new PostgresTableCounts(1, 1, 1), await fixture.CountsAsync(observer));
        Assert.AreSame(completion, owner.Completion);
    }

    [TestMethod]
    public async Task LaterStatementFailureRollsBackAllThreeEarlierWrites()
    {
        await using PostgresTransactionFixture fixture = await PostgresTransactionFixture.CreateAsync();
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("AcceptanceDatabase");
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        PostgresAcceptanceConsumer consumer = fixture.Consumer();
        Guid operation = Guid.NewGuid();
        await consumer.ExecuteAsync(owner.Context, operation, accepted: true);
        Assert.AreEqual(new PostgresTableCounts(0, 0, 0), await fixture.CountsAsync(observer));

        PostgresException failure = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
            consumer.Business.InsertAsync(owner.Context, operation));

        Assert.AreEqual(PostgresErrorCodes.UniqueViolation, failure.SqlState);
        Assert.IsTrue(owner.Context.IsRollbackOnly);
        Assert.AreEqual("transaction.work_failed", owner.Context.RollbackReasonCode);
        TransactionCompletion completion = await owner.CommitAsync();
        AssertRolledBack(completion, TransactionEndReason.RollbackOnly);
        Assert.IsFalse(completion.CommitAttempted);
        Assert.AreEqual(new PostgresTableCounts(0, 0, 0), await fixture.CountsAsync(observer));
    }

    [TestMethod]
    public async Task DeliberatelyCaughtDatabaseFailureMustBeExplicitlyMarkedAndCannotCommitEarlierWrites()
    {
        await using PostgresTransactionFixture fixture = await PostgresTransactionFixture.CreateAsync();
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("AcceptanceDatabase");
        PostgresAcceptanceConsumer consumer = fixture.Consumer();
        Guid operation = Guid.NewGuid();
        await consumer.ExecuteAsync(owner.Context, operation, accepted: true);
        bool caught = false;

        await owner.Context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            await using DbCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"INSERT INTO {fixture.BusinessTable} (operation_id, payload) VALUES (@operation, @payload)";
            PostgresAcceptanceParameter.Add(command, "operation", operation);
            PostgresAcceptanceParameter.Add(command, "payload", "duplicate fixture");
            try
            {
                await command.ExecuteNonQueryAsync(token);
            }
            catch (PostgresException failure) when (failure.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                caught = true;
                owner.Context.MarkRollbackOnly("fixture.caught_database_error");
            }
        });

        Assert.IsTrue(caught);
        Assert.AreEqual("fixture.caught_database_error", owner.Context.RollbackReasonCode);
        TransactionCompletion completion = await owner.CommitAsync();
        AssertRolledBack(completion, TransactionEndReason.RollbackOnly);
        Assert.IsFalse(completion.CommitAttempted);
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        Assert.AreEqual(new PostgresTableCounts(0, 0, 0), await fixture.CountsAsync(observer));
    }

    [TestMethod]
    public async Task BusinessRejectionCommitsOnlyTheResultAndDoesNotControlTheTransactionOutcome()
    {
        await using PostgresTransactionFixture fixture = await PostgresTransactionFixture.CreateAsync();
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("AcceptanceDatabase");
        var witness = new NativePairWitness();
        PostgresAcceptanceConsumer consumer = fixture.Consumer(witness.Observe);
        Guid operation = Guid.NewGuid();

        PostgresBusinessResult result = await consumer.ExecuteAsync(owner.Context, operation, accepted: false);
        TransactionCompletion completion = await owner.CommitAsync();

        Assert.IsFalse(result.Success);
        Assert.AreEqual("declined", result.Code);
        Assert.IsFalse(owner.Context.IsRollbackOnly);
        AssertCommitted(completion);
        CollectionAssert.AreEqual(new[] { "result" }, witness.Participants.ToArray());
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        Assert.AreEqual(new PostgresTableCounts(0, 1, 0), await fixture.CountsAsync(observer));
        Assert.IsFalse(await fixture.StoredResultIsSuccessfulAsync(observer, operation));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExplicitRollbackAndUncommittedDisposalLeaveNoRows(bool explicitRollback)
    {
        await using PostgresTransactionFixture fixture = await PostgresTransactionFixture.CreateAsync();
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("AcceptanceDatabase");
        await fixture.Consumer().ExecuteAsync(owner.Context, Guid.NewGuid(), accepted: true);

        if (explicitRollback)
        {
            TransactionCompletion rolledBack = await owner.RollbackAsync();
            Assert.AreSame(rolledBack, owner.Completion);
        }
        else
        {
            await owner.DisposeAsync();
        }

        Assert.IsNotNull(owner.Completion);
        AssertRolledBack(owner.Completion, explicitRollback ? TransactionEndReason.RollbackRequested : TransactionEndReason.UncommittedDisposal);
        Assert.AreEqual(LocalTransactionStatus.Completed, owner.Status);
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        Assert.AreEqual(new PostgresTableCounts(0, 0, 0), await fixture.CountsAsync(observer));
    }

    [TestMethod]
    [DataRow(TransactionIsolation.ReadCommitted, "read committed")]
    [DataRow(TransactionIsolation.RepeatableRead, "repeatable read")]
    [DataRow(TransactionIsolation.Serializable, "serializable")]
    public async Task AdvertisedIsolationMatchesTheActualServerTransaction(TransactionIsolation isolation, string expected)
    {
        await using PostgresTransactionFixture fixture = await PostgresTransactionFixture.CreateAsync();
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("AcceptanceDatabase", new LocalTransactionOptions { Isolation = isolation });

        string actual = await owner.Context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            await using DbCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SHOW transaction_isolation";
            return (string)(await command.ExecuteScalarAsync(token))!;
        });

        Assert.AreEqual(isolation, owner.Context.Isolation);
        Assert.AreEqual(expected, actual);
        AssertRolledBack(await owner.RollbackAsync(), TransactionEndReason.RollbackRequested);
    }

    [TestMethod]
    public async Task IndependentOwnersUseDistinctNativePairsAndLeaveTheApplicationDataSourceUsable()
    {
        await using PostgresTransactionFixture fixture = await PostgresTransactionFixture.CreateAsync();
        await using ILocalTransaction first = await fixture.Factory.BeginAsync("AcceptanceDatabase");
        await using ILocalTransaction second = await fixture.Factory.BeginAsync("AcceptanceDatabase");
        var firstPair = new NativePairWitness();

        await first.Context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            firstPair.Observe("first", connection, transaction);
            await SelectOneAsync(connection, transaction, token);
        });
        await second.Context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            firstPair.AssertDifferentPair(connection, transaction);
            Assert.AreNotEqual(firstPair.BackendPid, ((NpgsqlConnection)connection).ProcessID);
            await SelectOneAsync(connection, transaction, token);
        });

        AssertCommitted(await first.CommitAsync());
        AssertRolledBack(await second.RollbackAsync(), TransactionEndReason.RollbackRequested);
        await first.DisposeAsync();
        await second.DisposeAsync();

        // The same application-owned pool still supplies a working lease after both owners settle.
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("SELECT 42", observer);
        Assert.AreEqual(42, (int)(await command.ExecuteScalarAsync())!);
        Assert.AreEqual(new PostgresTableCounts(0, 0, 0), await fixture.CountsAsync(observer));
    }

    [TestMethod]
    public async Task ActiveNativeQueryCancellationIsConfirmedByAnIndependentObserverAndActuallySettles()
    {
        await using PostgresTransactionFixture fixture = await PostgresTransactionFixture.CreateAsync();
        await using NpgsqlConnection blocker = await fixture.Source.OpenConnectionAsync();
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("AcceptanceDatabase");
        await fixture.Consumer().ExecuteAsync(owner.Context, Guid.NewGuid(), accepted: true);
        using var cancellation = new CancellationTokenSource();
        long lockKey = Random.Shared.NextInt64(1, long.MaxValue);
        bool lockHeld = false;
        Task? work = null;
        var queryPid = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await AdvisoryLockAsync(blocker, lockKey, acquire: true);
            lockHeld = true;
            work = owner.Context.UseAdoNetAsync(async (connection, transaction, token) =>
            {
                await using (DbCommand identify = connection.CreateCommand())
                {
                    identify.Transaction = transaction;
                    identify.CommandText = "SELECT pg_backend_pid()";
                    queryPid.TrySetResult((int)(await identify.ExecuteScalarAsync(token))!);
                }
                await using DbCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "SELECT pg_advisory_xact_lock(@key)";
                PostgresAcceptanceParameter.Add(command, "key", lockKey);
                await command.ExecuteNonQueryAsync(token);
            }, cancellation.Token);

            int activePid = await queryPid.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.AreNotEqual(blocker.ProcessID, activePid);
            Assert.AreNotEqual(observer.ProcessID, activePid);
            await ConfirmBlockedByAsync(observer, activePid, blocker.ProcessID);
            Assert.IsFalse(work.IsCompleted, "The observed native advisory-lock query is still outstanding before cancellation.");

            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => work);

            Assert.IsTrue(work.IsCompleted);
            Assert.IsTrue(owner.Context.IsRollbackOnly);
            Assert.AreEqual("transaction.work_cancelled", owner.Context.RollbackReasonCode);
            Assert.IsNull(owner.Completion, "Callback settlement precedes owner cleanup and publication.");
            TransactionCompletion completion = await owner.CommitAsync();
            AssertRolledBack(completion, TransactionEndReason.RollbackOnly);
            Assert.IsFalse(completion.CommitAttempted);
            Assert.AreEqual(new PostgresTableCounts(0, 0, 0), await fixture.CountsAsync(observer));
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                if (lockHeld)
                {
                    await AdvisoryLockAsync(blocker, lockKey, acquire: false);
                }
            }
            finally
            {
                if (work is not null)
                {
                    try
                    {
                        await work;
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                    {
                        // Expected cancellation is joined, not abandoned behind an observation timeout.
                    }
                }
            }
        }
    }

    private static async Task ConfirmBlockedByAsync(NpgsqlConnection observer, int queryPid, int blockerPid)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        for (int attempt = 0; attempt < 512; attempt++)
        {
            await using var command = new NpgsqlCommand("SELECT @blocker = ANY(pg_blocking_pids(@query))", observer);
            command.Parameters.AddWithValue("blocker", blockerPid);
            command.Parameters.AddWithValue("query", queryPid);
            if ((bool)(await command.ExecuteScalarAsync(timeout.Token))!)
            {
                return;
            }
            // Spacing only bounds observer traffic; the server's blocking graph is the proof.
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
        Assert.Fail("The independent server observer did not confirm the fixture blocker before cancellation.");
    }

    private static async Task AdvisoryLockAsync(NpgsqlConnection connection, long key, bool acquire)
    {
        string sql = acquire ? "SELECT pg_advisory_lock(@key)" : "SELECT pg_advisory_unlock(@key)";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("key", key);
        object? result = await command.ExecuteScalarAsync();
        if (!acquire)
        {
            Assert.IsTrue(result is true, "The fixture releases only its own acquired advisory lock.");
        }
    }

    private static async Task SelectOneAsync(DbConnection connection, DbTransaction transaction, CancellationToken token)
    {
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1";
        Assert.AreEqual(1, (int)(await command.ExecuteScalarAsync(token))!);
    }

    private static void AssertCommitted(TransactionCompletion completion)
    {
        Assert.AreEqual(TransactionOutcome.Committed, completion.Outcome);
        Assert.IsTrue(completion.CommitAttempted);
        Assert.IsFalse(completion.RollbackAttempted);
        Assert.IsNull(completion.Error);
        Assert.HasCount(0, completion.CleanupErrors);
    }

    private static void AssertRolledBack(TransactionCompletion completion, TransactionEndReason reason)
    {
        Assert.AreEqual(TransactionOutcome.NotCommitted, completion.Outcome);
        Assert.AreEqual(reason, completion.EndReason);
        Assert.IsFalse(completion.CommitAttempted);
        Assert.IsTrue(completion.RollbackAttempted);
        Assert.IsTrue(completion.RollbackConfirmed);
        Assert.HasCount(0, completion.CleanupErrors);
    }

    private sealed class NativePairWitness
    {
        private WeakReference<DbConnection>? firstConnection;
        private WeakReference<DbTransaction>? firstTransaction;

        internal List<string> Participants { get; } = [];
        internal int BackendPid
        {
            get; private set;
        }

        internal void Observe(string participant, DbConnection connection, DbTransaction transaction)
        {
            Assert.IsTrue(connection is NpgsqlConnection);
            Assert.IsTrue(transaction is NpgsqlTransaction);
            if (firstConnection is null)
            {
                firstConnection = new(connection);
                firstTransaction = new(transaction);
                BackendPid = ((NpgsqlConnection)connection).ProcessID;
            }
            else
            {
                Assert.IsTrue(firstConnection.TryGetTarget(out DbConnection? originalConnection));
                Assert.IsTrue(firstTransaction!.TryGetTarget(out DbTransaction? originalTransaction));
                Assert.AreSame(originalConnection, connection);
                Assert.AreSame(originalTransaction, transaction);
            }
            Participants.Add(participant);
        }

        internal void AssertDifferentPair(DbConnection connection, DbTransaction transaction)
        {
            Assert.IsNotNull(firstConnection);
            Assert.IsNotNull(firstTransaction);
            Assert.IsTrue(firstConnection.TryGetTarget(out DbConnection? originalConnection));
            Assert.IsTrue(firstTransaction.TryGetTarget(out DbTransaction? originalTransaction));
            Assert.AreNotSame(originalConnection, connection);
            Assert.AreNotSame(originalTransaction, transaction);
        }
    }
}

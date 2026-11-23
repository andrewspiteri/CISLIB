using System.Data.Common;
using CIS.Coordination;
using CIS.Coordination.PostgreSql;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Coordination.PostgreSql;

[TestClass]
[DoNotParallelize]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
public sealed class PostgresCoordinationGuardTests
{
    [TestMethod]
    public async Task GrantThatExpiresDuringTheRealRowLockWaitIsRejectedUsingPostLockAuthorityTime()
    {
        await using PostgresCoordinationFixture fixture = await PostgresCoordinationFixture.CreateAsync();
        CoordinationGrant grant = await fixture.AcquireAsync();
        await using ILocalTransaction blocker = await fixture.Factory.BeginAsync("Coordination");
        await ExecuteAsync(blocker.Context, $"SELECT 1 FROM {fixture.Grants} FOR UPDATE");
        int blockerPid = await PostgresCoordinationFixture.PidAsync(blocker.Context);
        await using ILocalTransaction waiter = await fixture.Factory.BeginAsync("Coordination");
        int waiterPid = await PostgresCoordinationFixture.PidAsync(waiter.Context);
        DateTimeOffset transactionStart = await waiter.Context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            await using DbCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT current_timestamp";
            await using DbDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            return reader.GetFieldValue<DateTimeOffset>(0);
        });
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        Task<CoordinationGuardResult>? pending = null;
        try
        {
            pending = fixture.Authority.ValidateAsync(waiter.Context, grant);
            await PostgresCoordinationFixture.ConfirmBlockedAsync(observer, waiterPid, blockerPid);
            DateTimeOffset expiry = await blocker.Context.UseAdoNetAsync(async (connection, transaction, token) =>
            {
                await using DbCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"UPDATE {fixture.Grants} SET expires_at = clock_timestamp() - INTERVAL '1 microsecond' RETURNING expires_at";
                await using DbDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                return reader.GetFieldValue<DateTimeOffset>(0);
            });
            Assert.IsTrue(expiry > transactionStart, "The waited grant expires after this transaction's frozen now() value.");
            Assert.AreEqual(TransactionOutcome.Committed, (await blocker.CommitAsync()).Outcome);
            CoordinationGuardResult rejected = await pending;
            Assert.AreEqual(CoordinationRejectionReason.Expired, rejected.RejectionReason);
            Assert.IsTrue(waiter.Context.IsRollbackOnly);
            Assert.AreEqual(TransactionOutcome.NotCommitted, (await waiter.CommitAsync()).Outcome);
            Assert.AreEqual(0L, await fixture.ScalarAsync($"SELECT count(*) FROM {fixture.Business}"));
            Assert.AreEqual(CoordinationRejectionReason.Expired,
                (await fixture.Authority.RenewAsync(grant, TimeSpan.FromSeconds(30))).RejectionReason);
            CoordinationGrant successor = await fixture.AcquireAsync(instance: grant.Acquisition.InstanceId);
            Assert.IsGreaterThan(grant.Generation, successor.Generation);
        }
        finally
        {
            // Release the blocker before joining/disposal of its actual waiting callback.
            await blocker.DisposeAsync();
            if (pending is not null)
            {
                await pending;
            }
        }
    }

    [TestMethod]
    public async Task GuardedTransactionDelaysTakeoverUntilCommitWhileAnIndependentKeyStillProgresses()
    {
        await using PostgresCoordinationFixture fixture = await PostgresCoordinationFixture.CreateAsync();
        CoordinationGrant first = await fixture.AcquireAsync();
        await using ILocalTransaction business = await fixture.Factory.BeginAsync("Coordination");
        int businessPid = await PostgresCoordinationFixture.PidAsync(business.Context);
        Assert.IsTrue((await fixture.Authority.ValidateAsync(business.Context, first)).Admitted);
        await ExecuteAsync(business.Context, $"""
            INSERT INTO {fixture.Business} (effect_id, generation) VALUES ('{Guid.NewGuid():D}'::uuid, {first.Generation});
            UPDATE {fixture.Grants} SET expires_at = clock_timestamp() - INTERVAL '1 microsecond';
            """);
        var observedFactory = new PostgresCoordinationAuthorityTests.ObservedFactory(fixture.Factory);
        var contender = new PostgreSqlCoordinationProvider(observedFactory, "Coordination", new()
        {
            AuthorityId = fixture.AuthorityId,
            Schema = fixture.Schema
        });
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        Task<CoordinationAcquireResult>? pending = null;
        try
        {
            pending = contender.AcquireAsync(fixture.Request(instance: "successor"));
            int contenderPid = await observedFactory.Pid.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await PostgresCoordinationFixture.ConfirmBlockedAsync(observer, contenderPid, businessPid);
            Assert.IsFalse(pending.IsCompleted);
            Assert.AreEqual(CoordinationAcquireOutcome.Acquired,
                (await fixture.Authority.AcquireAsync(fixture.Request("independent", "other-duty"))).Outcome);
            Assert.AreEqual(TransactionOutcome.Committed, (await business.CommitAsync()).Outcome);
            CoordinationAcquireResult acquired = await pending;
            Assert.AreEqual(CoordinationAcquireOutcome.Acquired, acquired.Outcome);
            Assert.IsNotNull(acquired.Grant);
            Assert.IsGreaterThan(first.Generation, acquired.Grant.Generation);
            Assert.AreEqual(1L, await fixture.ScalarAsync($"SELECT count(*) FROM {fixture.Business} WHERE generation = {first.Generation}"));
            await using ILocalTransaction stale = await fixture.Factory.BeginAsync("Coordination");
            Assert.AreEqual(CoordinationRejectionReason.Superseded, (await fixture.Authority.ValidateAsync(stale.Context, first)).RejectionReason);
        }
        finally
        {
            await business.DisposeAsync();
            if (pending is not null)
            {
                await pending;
            }
        }
    }

    [TestMethod]
    public async Task CancellationOfABlockedAcquisitionJoinsTheNativeAttemptAndNeverBlindlyReplaysIt()
    {
        await using PostgresCoordinationFixture fixture = await PostgresCoordinationFixture.CreateAsync(boundary: true);
        CoordinationGrant first = await fixture.AcquireAsync();
        await using ILocalTransaction blocker = await fixture.Factory.BeginAsync("Coordination");
        Assert.IsTrue((await fixture.Authority.ValidateAsync(blocker.Context, first)).Admitted);
        int blockerPid = await PostgresCoordinationFixture.PidAsync(blocker.Context);
        var observedFactory = new PostgresCoordinationAuthorityTests.ObservedFactory(fixture.Factory);
        var contender = new PostgreSqlCoordinationProvider(observedFactory, "Coordination", new()
        {
            AuthorityId = fixture.AuthorityId,
            Schema = fixture.Schema
        });
        CoordinationAcquireRequest request = fixture.Request(instance: "cancelled");
        using var cancellation = new CancellationTokenSource();
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        Task<CoordinationAcquireResult>? pending = null;
        try
        {
            pending = contender.AcquireAsync(request, cancellation.Token);
            int waiterPid = await observedFactory.Pid.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await PostgresCoordinationFixture.ConfirmBlockedAsync(observer, waiterPid, blockerPid);
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending);
            Assert.AreEqual(1, observedFactory.Begins);
            Assert.AreEqual(1, fixture.Boundary!.ConfirmedNativeCommits, "Only the original successful grant committed.");
        }
        finally
        {
            cancellation.Cancel();
            await blocker.DisposeAsync();
            if (pending is not null)
            {
                try
                {
                    await pending;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                }
            }
        }
        Assert.AreEqual(CoordinationObservationOutcome.NotObserved, (await fixture.Authority.ResolveAsync(request.Acquisition)).Outcome);
        Assert.AreEqual(CoordinationObservationOutcome.FoundLive, (await fixture.Authority.ResolveAsync(first.Acquisition)).Outcome);
    }

    private static Task ExecuteAsync(ILocalTransactionContext context, string sql) => context.UseAdoNetAsync(async (connection, transaction, token) =>
    {
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(token);
    });
}

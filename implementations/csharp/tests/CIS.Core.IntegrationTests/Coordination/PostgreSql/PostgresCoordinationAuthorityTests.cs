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
public sealed class PostgresCoordinationAuthorityTests
{
    [TestMethod]
    public async Task ContendersGetOneCommittedGrantAndOldMutationsCannotChangeItsSuccessor()
    {
        await using PostgresCoordinationFixture fixture = await PostgresCoordinationFixture.CreateAsync();
        CoordinationAcquireRequest[] requests = Enumerable.Range(0, 4)
            .Select(index => fixture.Request(instance: "instance-" + index)).ToArray();
        CoordinationAcquireResult[] results = await Task.WhenAll(requests.Select(request => fixture.Authority.AcquireAsync(request)));
        Assert.AreEqual(1, results.Count(result => result.Outcome == CoordinationAcquireOutcome.Acquired));
        Assert.AreEqual(3, results.Count(result => result.Outcome == CoordinationAcquireOutcome.Busy));
        CoordinationGrant winner = results.Single(result => result.Outcome == CoordinationAcquireOutcome.Acquired).Grant!;
        foreach (CoordinationAcquireResult busy in results.Where(result => result.Outcome == CoordinationAcquireOutcome.Busy))
        {
            Assert.IsNotNull(busy.ObservedOwner);
            Assert.AreEqual(winner.Generation, busy.ObservedOwner.Generation);
            Assert.AreEqual(winner.Acquisition.InstanceId, busy.ObservedOwner.InstanceId);
        }
        CoordinationObservation observed = await fixture.Authority.ResolveAsync(winner.Acquisition);
        Assert.AreEqual(CoordinationObservationOutcome.FoundLive, observed.Outcome);
        Assert.AreEqual(winner, observed.Grant);
        CoordinationAcquireRequest loser = requests.First(request => request.Acquisition.AcquisitionId != winner.Acquisition.AcquisitionId);
        Assert.AreEqual(CoordinationObservationOutcome.NotObserved, (await fixture.Authority.ResolveAsync(loser.Acquisition)).Outcome);

        CoordinationMutationResult renewal = await fixture.Authority.RenewAsync(winner, TimeSpan.FromSeconds(30));
        Assert.AreEqual(CoordinationMutationOutcome.Succeeded, renewal.Outcome);
        Assert.IsNotNull(renewal.Grant);
        Assert.AreEqual(winner.Generation, renewal.Grant.Generation);
        Assert.AreEqual(winner.Acquisition, renewal.Grant.Acquisition);
        Assert.IsTrue(renewal.RemainingValidity > TimeSpan.Zero);
        Assert.AreEqual(CoordinationMutationOutcome.Succeeded, (await fixture.Authority.ReleaseAsync(winner)).Outcome);
        Assert.AreEqual(CoordinationRejectionReason.Released,
            (await fixture.Authority.RenewAsync(winner, TimeSpan.FromSeconds(30))).RejectionReason);

        CoordinationGrant successor = await fixture.AcquireAsync(instance: winner.Acquisition.InstanceId);
        Assert.IsGreaterThan(winner.Generation, successor.Generation);
        Assert.AreNotEqual(winner.Acquisition.AcquisitionId, successor.Acquisition.AcquisitionId);
        Assert.AreEqual(CoordinationRejectionReason.Superseded, (await fixture.Authority.ReleaseAsync(winner)).RejectionReason);
        Assert.AreEqual(CoordinationRejectionReason.Superseded, (await fixture.Authority.PruneAsync(winner)).RejectionReason);
        await using ILocalTransaction stale = await fixture.Factory.BeginAsync("Coordination");
        CoordinationGuardResult guard = await fixture.Authority.ValidateAsync(stale.Context, winner);
        Assert.AreEqual(CoordinationRejectionReason.Superseded, guard.RejectionReason);
        Assert.IsTrue(stale.Context.IsRollbackOnly);
        Assert.AreEqual(TransactionOutcome.NotCommitted, (await stale.CommitAsync()).Outcome);
        Assert.AreEqual(successor, (await fixture.Authority.ResolveAsync(successor.Acquisition)).Grant);
    }

    [TestMethod]
    public async Task ReleasePruneAndRecreationAcrossDifferentPooledSessionsKeepIncreasingGenerations()
    {
        await using PostgresCoordinationFixture fixture = await PostgresCoordinationFixture.CreateAsync();
        var firstFactory = new ObservedFactory(fixture.Factory);
        var firstAuthority = new PostgreSqlCoordinationProvider(firstFactory, "Coordination", new()
        {
            AuthorityId = fixture.AuthorityId,
            Schema = fixture.Schema
        });
        CoordinationGrant first = (await firstAuthority.AcquireAsync(fixture.Request())).Grant!;
        int firstPid = await firstFactory.Pid.Task;
        Assert.AreEqual(CoordinationMutationOutcome.Succeeded, (await fixture.Authority.ReleaseAsync(first)).Outcome);
        Assert.AreEqual(CoordinationMutationOutcome.Succeeded, (await fixture.Authority.PruneAsync(first)).Outcome);

        // Hold the first pool session as an idle test observer so the next acquisition must use another.
        var held = new List<NpgsqlConnection>();
        try
        {
            bool heldFirst = false;
            for (int index = 0; index < 5 && !heldFirst; index++)
            {
                NpgsqlConnection connection = await fixture.Source.OpenConnectionAsync();
                held.Add(connection);
                heldFirst = connection.ProcessID == firstPid;
            }
            Assert.IsTrue(heldFirst);
            var secondFactory = new ObservedFactory(fixture.Factory);
            var secondAuthority = new PostgreSqlCoordinationProvider(secondFactory, "Coordination", new()
            {
                AuthorityId = fixture.AuthorityId,
                Schema = fixture.Schema
            });
            CoordinationGrant second = (await secondAuthority.AcquireAsync(fixture.Request())).Grant!;
            Assert.AreNotEqual(firstPid, await secondFactory.Pid.Task);
            Assert.IsGreaterThan(first.Generation, second.Generation);
            Assert.AreEqual(CoordinationRejectionReason.Superseded, (await fixture.Authority.ReleaseAsync(first)).RejectionReason);
            Assert.AreEqual(CoordinationMutationOutcome.Succeeded, (await fixture.Authority.ReleaseAsync(second)).Outcome);
            Assert.AreEqual(CoordinationMutationOutcome.Succeeded, (await fixture.Authority.PruneAsync(second)).Outcome);
            CoordinationGrant third = await fixture.AcquireAsync();
            Assert.IsGreaterThan(second.Generation, third.Generation);
        }
        finally
        {
            foreach (NpgsqlConnection connection in held)
            {
                await connection.DisposeAsync();
            }
        }
        Assert.AreEqual(1L, await fixture.ScalarAsync($"SELECT seqcache FROM pg_sequence WHERE seqrelid = '{fixture.Qualified("grant_generation")}'::regclass"));
        Assert.AreEqual(0L, await fixture.ScalarAsync($"SELECT count(*) FROM pg_sequence WHERE seqrelid = '{fixture.Qualified("grant_generation")}'::regclass AND seqcycle"));
    }

    [TestMethod]
    public async Task UnknownNativeCommitCanBeObservedExactlyAndCleanupFailureDoesNotEraseConfirmedAcquisition()
    {
        await using PostgresCoordinationFixture fixture = await PostgresCoordinationFixture.CreateAsync(boundary: true);
        CoordinationAcquireRequest request = fixture.Request();
        fixture.Boundary!.FailAfterNextCommit();
        CoordinationAcquireResult uncertain = await fixture.Authority.AcquireAsync(request);
        Assert.AreEqual(CoordinationAcquireOutcome.Indeterminate, uncertain.Outcome);
        Assert.IsNull(uncertain.Grant);
        CoordinationObservation recovered = await fixture.Authority.ResolveAsync(request.Acquisition);
        Assert.AreEqual(CoordinationObservationOutcome.FoundLive, recovered.Outcome);
        Assert.IsNotNull(recovered.Grant);
        Assert.AreEqual(1, fixture.Boundary.ConfirmedNativeCommits);

        fixture.Boundary.FailAfterNextCommit();
        Assert.AreEqual(CoordinationMutationOutcome.Indeterminate,
            (await fixture.Authority.RenewAsync(recovered.Grant, TimeSpan.FromSeconds(30))).Outcome);
        Assert.AreEqual(CoordinationObservationOutcome.FoundLive, (await fixture.Authority.ResolveAsync(request.Acquisition)).Outcome);
        fixture.Boundary.FailAfterNextCommit();
        Assert.AreEqual(CoordinationMutationOutcome.Indeterminate, (await fixture.Authority.ReleaseAsync(recovered.Grant)).Outcome);
        Assert.AreEqual(CoordinationObservationOutcome.NotObserved, (await fixture.Authority.ResolveAsync(request.Acquisition)).Outcome);

        fixture.Boundary.FailAfterNextTransactionDisposal();
        CoordinationGrant next = await fixture.AcquireAsync();
        Assert.IsGreaterThan(recovered.Grant.Generation, next.Generation);
        Assert.AreEqual(4, fixture.Boundary.ConfirmedNativeCommits);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InstalledAuthorityRefusesMissingOrCorruptAllocatorAndNeverRepairsItByReinstallation(bool missing)
    {
        await using PostgresCoordinationFixture fixture = await PostgresCoordinationFixture.CreateAsync();
        CoordinationGrant original = await fixture.AcquireAsync();
        Assert.IsGreaterThan(0L, original.Generation);
        await fixture.ExecuteAsync(missing
            ? $"DROP SEQUENCE {fixture.Qualified("grant_generation")}"
            : $"ALTER SEQUENCE {fixture.Qualified("grant_generation")} CACHE 2");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Authority.ValidateAsync());
        await Assert.ThrowsExactlyAsync<PostgresException>(() => fixture.ExecuteAsync(
            PostgreSqlCoordinationSchema.GetInstallationSql(fixture.AuthorityId, fixture.Schema)));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Authority.AcquireAsync(fixture.Request("other")));
        Assert.AreEqual(missing ? 0L : 2L, await fixture.ScalarAsync(missing
            ? $"SELECT count(*) FROM pg_class WHERE oid = to_regclass('{fixture.Qualified("grant_generation")}')"
            : $"SELECT seqcache FROM pg_sequence WHERE seqrelid = '{fixture.Qualified("grant_generation")}'::regclass"));
    }

    [TestMethod]
    public async Task ExhaustedAllocatorRefusesAcquisitionWithoutWrappingOrChangingTheExistingGrant()
    {
        await using PostgresCoordinationFixture fixture = await PostgresCoordinationFixture.CreateAsync();
        CoordinationGrant original = await fixture.AcquireAsync();
        await fixture.ExecuteAsync($"SELECT setval('{fixture.Qualified("grant_generation")}'::regclass, 9223372036854775807, true)");
        CoordinationAcquireResult refused = await fixture.Authority.AcquireAsync(fixture.Request("other"));
        Assert.AreEqual(CoordinationAcquireOutcome.Unavailable, refused.Outcome);
        Assert.IsNull(refused.Grant);
        Assert.AreEqual(original, (await fixture.Authority.ResolveAsync(original.Acquisition)).Grant);
        Assert.AreEqual(1L, await fixture.ScalarAsync($"SELECT count(*) FROM {fixture.Grants}"),
            "The failed transaction must roll back its newly created key placeholder.");
    }

    [TestMethod]
    public async Task AnotherAuthorityOrDatabaseCannotAdmitTheGrantAndCannotCommitProtectedEffects()
    {
        await using PostgresCoordinationFixture fixture = await PostgresCoordinationFixture.CreateAsync();
        CoordinationGrant grant = await fixture.AcquireAsync();
        PostgreSqlCoordinationProvider wrong = fixture.NewAuthority(Guid.NewGuid());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => wrong.ValidateAsync());
        await using ILocalTransaction authorityOwner = await fixture.Factory.BeginAsync("Coordination");
        Assert.AreEqual(CoordinationRejectionReason.WrongAuthority, (await wrong.ValidateAsync(authorityOwner.Context, grant)).RejectionReason);
        Assert.AreEqual(TransactionOutcome.NotCommitted, (await authorityOwner.CommitAsync()).Outcome);
        await using PostgresCoordinationFixture independent = await PostgresCoordinationFixture.CreateAsync();
        await using ILocalTransaction independentOwner = await independent.Factory.BeginAsync("Coordination");
        Assert.AreEqual(CoordinationRejectionReason.WrongAuthority,
            (await independent.Authority.ValidateAsync(independentOwner.Context, grant)).RejectionReason);
        Assert.AreEqual(TransactionOutcome.NotCommitted, (await independentOwner.CommitAsync()).Outcome);
        await using ILocalTransaction databaseOwner = await fixture.Factory.BeginAsync("Other");
        Assert.AreEqual(CoordinationRejectionReason.WrongDatabase,
            (await fixture.Authority.ValidateAsync(databaseOwner.Context, grant)).RejectionReason);
        Assert.AreEqual(TransactionOutcome.NotCommitted, (await databaseOwner.CommitAsync()).Outcome);
        await using ILocalTransaction isolationOwner = await fixture.Factory.BeginAsync("Coordination",
            new LocalTransactionOptions { Isolation = TransactionIsolation.RepeatableRead });
        Assert.AreEqual(CoordinationRejectionReason.InvalidTransaction,
            (await fixture.Authority.ValidateAsync(isolationOwner.Context, grant)).RejectionReason);
        Assert.AreEqual(TransactionOutcome.NotCommitted, (await isolationOwner.CommitAsync()).Outcome);
        Assert.AreEqual(0L, await fixture.ScalarAsync($"SELECT count(*) FROM {fixture.Business}"));
    }

    internal sealed class ObservedFactory(ILocalTransactionFactory inner) : ILocalTransactionFactory
    {
        internal int Begins;
        internal TaskCompletionSource<int> Pid { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ILocalTransaction> BeginAsync(string databaseName, CancellationToken cancellationToken = default)
            => BeginAsync(databaseName, new LocalTransactionOptions(), cancellationToken);

        public async Task<ILocalTransaction> BeginAsync(string databaseName, LocalTransactionOptions options,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Begins);
            ILocalTransaction owner = await inner.BeginAsync(databaseName, options, cancellationToken);
            try
            {
                Pid.TrySetResult(await PostgresCoordinationFixture.PidAsync(owner.Context));
                return owner;
            }
            catch
            {
                await owner.DisposeAsync();
                throw;
            }
        }
    }
}

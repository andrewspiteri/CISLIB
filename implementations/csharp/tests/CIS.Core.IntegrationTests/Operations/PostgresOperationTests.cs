using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CIS.Operations;
using CIS.Operations.PostgreSql;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Operations;

[TestClass]
[DoNotParallelize]
public sealed class PostgresOperationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task JournalBusinessAndOutgoingBecomeVisibleTogetherAndFreshLookupRecoversOriginal()
    {
        await using PostgresOperationFixture fixture = await PostgresOperationFixture.CreateAsync();
        await using AsyncServiceScope scope = fixture.CreateScope();
        IDurableOperationService service = scope.ServiceProvider.GetRequiredService<IDurableOperationService>();
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Ledger");
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        var elapsed = Stopwatch.StartNew();
        int handlerCalls = 0;
        var submitted = new OperationRequest<DebitInput>("Debit", "alpha", "key", new("forged-payload-account", 10, true));
        OperationExecution<DebitReceipt> prepared = await service.ExecuteAsync<DebitInput, DebitReceipt>(owner.Context, submitted,
            async (context, input, token) =>
            {
                Assert.AreSame(owner.Context, context);
                Assert.AreEqual("account-alpha", input.Account, "The owned business target must be the authorized resolved resource.");
                Assert.AreNotEqual(submitted.Input.Account, input.Account);
                handlerCalls++;
                return await fixture.HandleAsync(context, input, token);
            });
        Assert.AreEqual(OperationExecutionDisposition.Prepared, prepared.Disposition);
        Assert.IsNotNull(prepared.Decision);
        Assert.IsNotNull(prepared.Decision.Value);
        Assert.IsNull(owner.Completion, "Prepared has not completed the application-owned transaction.");
        Assert.AreEqual(new OperationTableCounts(0, 0, 0), await fixture.CountsAsync(observer));
        OperationLookup<DebitReceipt> uncommitted = await service.GetResultAsync<DebitReceipt>(new("Debit", "alpha", "key"));
        Assert.AreEqual(OperationLookupDisposition.NotFound, uncommitted.Disposition);
        Assert.AreEqual(TransactionOutcome.NotCommitted, uncommitted.ReadScopeCompletion?.Outcome);

        AssertCommitted(await owner.CommitAsync());
        Assert.AreEqual(new OperationTableCounts(1, 1, 1), await fixture.CountsAsync(observer));
        await using (var nativeIdentity = new NpgsqlCommand($"""
            SELECT journal.claim_xid = business.native_xid AND business.native_xid = outgoing.native_xid
            FROM {fixture.JournalTable} journal CROSS JOIN {fixture.BusinessTable} business CROSS JOIN {fixture.OutgoingTable} outgoing
            """, observer))
        {
            Assert.AreEqual(true, await nativeIdentity.ExecuteScalarAsync(), "All three rows were written in the same native transaction.");
        }
        OperationLookup<DebitReceipt> found = await service.GetResultAsync<DebitReceipt>(new("Debit", "alpha", "key"));
        Assert.AreEqual(OperationLookupDisposition.Found, found.Disposition);
        Assert.AreEqual(prepared.Decision.Value, found.Decision?.Value);
        await using ILocalTransaction retry = await fixture.Factory.BeginAsync("Ledger");
        OperationExecution<DebitReceipt> replay = await service.ExecuteAsync<DebitInput, DebitReceipt>(retry.Context, PostgresOperationFixture.Request(),
            (_, _, _) => throw new InvalidOperationException("A committed matching retry must not invoke its handler."));
        Assert.AreEqual(OperationExecutionDisposition.Replayed, replay.Disposition,
            "Different raw payload account text cannot change the normalized authorized account or its fingerprint.");
        Assert.AreEqual(prepared.Decision.Value, replay.Decision?.Value);
        Assert.AreEqual(1, handlerCalls);
        await retry.RollbackAsync();
        elapsed.Stop();

        await using var shape = new NpgsqlCommand($"""
            SELECT current_setting('server_version'), pg_column_size(application) + pg_column_size(environment)
                + pg_column_size(scope) + pg_column_size(operation_name) + pg_column_size(idempotency_key),
                octet_length(result_payload), pg_column_size(journal)
            FROM {fixture.JournalTable} journal
            """, observer);
        await using NpgsqlDataReader row = await shape.ExecuteReaderAsync();
        Assert.IsTrue(await row.ReadAsync());
        TestContext.WriteLine("RM03_WORKLOAD " + JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            serverVersion = row.GetString(0),
            identityStoredBytes = row.GetInt32(1),
            resultPayloadBytes = row.GetInt32(2),
            journalRowStoredBytes = row.GetInt32(3),
            tableShape = "four ordinary logged tables: schema_version, operation_journal, business_record, outgoing_record",
            committedRows = new
            {
                journal = 1,
                business = 1,
                outgoing = 1
            },
            admittedConcurrentExecutions = 1,
            maximumSimultaneousLeases = 3,
            elapsedMilliseconds = elapsed.Elapsed.TotalMilliseconds,
            coreSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(IDurableOperationService).Assembly.Location))),
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            observation = "one end-to-end prepare/commit/independent lookup/replay, not a benchmark or installation-only proof"
        }));
    }

    [TestMethod]
    public async Task FinalBusinessRejectionCommitsItsJournalAndReplaysTheOriginalError()
    {
        await using PostgresOperationFixture fixture = await PostgresOperationFixture.CreateAsync();
        await using AsyncServiceScope scope = fixture.CreateScope();
        IDurableOperationService service = scope.ServiceProvider.GetRequiredService<IDurableOperationService>();
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Ledger");
        OperationExecution<DebitReceipt> rejected = await service.ExecuteAsync<DebitInput, DebitReceipt>(owner.Context,
            PostgresOperationFixture.Request(accepted: false), fixture.HandleAsync);
        Assert.AreEqual(OperationBusinessDecision.Rejected, rejected.Decision?.Decision);
        AssertCommitted(await owner.CommitAsync());
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        Assert.AreEqual(new OperationTableCounts(1, 0, 0), await fixture.CountsAsync(observer));
        await using ILocalTransaction retry = await fixture.Factory.BeginAsync("Ledger");
        OperationExecution<DebitReceipt> replay = await service.ExecuteAsync<DebitInput, DebitReceipt>(retry.Context,
            PostgresOperationFixture.Request(accepted: false), (_, _, _) => throw new InvalidOperationException("No handler on replay."));
        Assert.AreEqual(OperationExecutionDisposition.Replayed, replay.Disposition);
        Assert.AreEqual(rejected.Decision?.Error, replay.Decision?.Error);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RollbackAndUnfinishedDisposalRemoveEveryProtectedEffectAndAllowFreshExecution(bool explicitRollback)
    {
        await using PostgresOperationFixture fixture = await PostgresOperationFixture.CreateAsync();
        await using AsyncServiceScope scope = fixture.CreateScope();
        IDurableOperationService service = scope.ServiceProvider.GetRequiredService<IDurableOperationService>();
        await using ILocalTransaction first = await fixture.Factory.BeginAsync("Ledger");
        OperationExecution<DebitReceipt> abandoned = await service.ExecuteAsync<DebitInput, DebitReceipt>(first.Context, PostgresOperationFixture.Request(), fixture.HandleAsync);
        Assert.AreEqual(OperationExecutionDisposition.Prepared, abandoned.Disposition);
        if (explicitRollback)
        {
            await first.RollbackAsync();
        }
        else
        {
            await first.DisposeAsync();
        }
        Assert.AreEqual(TransactionOutcome.NotCommitted, first.Completion?.Outcome);
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        Assert.AreEqual(new OperationTableCounts(0, 0, 0), await fixture.CountsAsync(observer));
        await using ILocalTransaction fresh = await fixture.Factory.BeginAsync("Ledger");
        OperationExecution<DebitReceipt> actual = await service.ExecuteAsync<DebitInput, DebitReceipt>(fresh.Context, PostgresOperationFixture.Request(), fixture.HandleAsync);
        Assert.AreEqual(OperationExecutionDisposition.Prepared, actual.Disposition);
        Assert.AreNotEqual(abandoned.Decision?.Value?.Identifier, actual.Decision?.Value?.Identifier);
        AssertCommitted(await fresh.CommitAsync());
        Assert.AreEqual(new OperationTableCounts(1, 1, 1), await fixture.CountsAsync(observer));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task IndependentSessionsArbitrateOneCommittedEffect(bool differentInput, bool rollbackWinner)
    {
        await using PostgresOperationFixture fixture = await PostgresOperationFixture.CreateAsync();
        await using AsyncServiceScope firstScope = fixture.CreateScope();
        await using AsyncServiceScope secondScope = fixture.CreateScope();
        IDurableOperationService firstService = firstScope.ServiceProvider.GetRequiredService<IDurableOperationService>();
        IDurableOperationService secondService = secondScope.ServiceProvider.GetRequiredService<IDurableOperationService>();
        await using ILocalTransaction winner = await fixture.Factory.BeginAsync("Ledger");
        await using ILocalTransaction contender = await fixture.Factory.BeginAsync("Ledger");
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        int firstPid = await PostgresOperationFixture.PidAsync(winner.Context);
        int secondPid = await PostgresOperationFixture.PidAsync(contender.Context);
        Assert.AreNotEqual(firstPid, secondPid);
        Assert.AreNotEqual(observer.ProcessID, firstPid);
        Assert.AreNotEqual(observer.ProcessID, secondPid);
        OperationExecution<DebitReceipt> first = await firstService.ExecuteAsync<DebitInput, DebitReceipt>(winner.Context,
            PostgresOperationFixture.Request(), fixture.HandleAsync);
        int laterHandlers = 0;
        using var cancellation = new CancellationTokenSource();
        Task<OperationExecution<DebitReceipt>>? pending = null;
        try
        {
            pending = secondService.ExecuteAsync<DebitInput, DebitReceipt>(contender.Context, PostgresOperationFixture.Request(differentInput ? 11 : 10),
                async (context, input, token) =>
                {
                    laterHandlers++;
                    return await fixture.HandleAsync(context, input, token);
                }, cancellation.Token);
            await PostgresOperationFixture.ConfirmBlockedAsync(observer, secondPid, firstPid);
            Assert.IsFalse(pending.IsCompleted);
            Assert.AreEqual(0, laterHandlers);
            Assert.AreEqual(new OperationTableCounts(0, 0, 0), await fixture.CountsAsync(observer));
            if (rollbackWinner)
            {
                await winner.RollbackAsync();
            }
            else
            {
                AssertCommitted(await winner.CommitAsync());
            }
            OperationExecution<DebitReceipt> settled = await pending;
            Assert.AreEqual(rollbackWinner ? OperationExecutionDisposition.Prepared
                : differentInput ? OperationExecutionDisposition.KeyConflict : OperationExecutionDisposition.Replayed, settled.Disposition);
            Assert.AreEqual(rollbackWinner ? 1 : 0, laterHandlers);
            if (rollbackWinner)
            {
                AssertCommitted(await contender.CommitAsync());
            }
            else
            {
                if (!differentInput)
                {
                    Assert.AreEqual(first.Decision?.Value, settled.Decision?.Value);
                }
                await contender.RollbackAsync();
            }
            Assert.AreEqual(new OperationTableCounts(1, 1, 1), await fixture.CountsAsync(observer));
        }
        finally
        {
            cancellation.Cancel();
            await winner.DisposeAsync();
            await JoinDuringCleanupAsync(pending, cancellation.Token);
        }
    }

    [TestMethod]
    public async Task CancellationOfAnObservedBlockedClaimSettlesBeforeFreshRecovery()
    {
        await using PostgresOperationFixture fixture = await PostgresOperationFixture.CreateAsync();
        await using AsyncServiceScope scope = fixture.CreateScope();
        IDurableOperationService service = scope.ServiceProvider.GetRequiredService<IDurableOperationService>();
        await using ILocalTransaction winner = await fixture.Factory.BeginAsync("Ledger");
        await using ILocalTransaction contender = await fixture.Factory.BeginAsync("Ledger");
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        int winnerPid = await PostgresOperationFixture.PidAsync(winner.Context);
        int contenderPid = await PostgresOperationFixture.PidAsync(contender.Context);
        OperationExecution<DebitReceipt> first = await service.ExecuteAsync<DebitInput, DebitReceipt>(winner.Context, PostgresOperationFixture.Request(), fixture.HandleAsync);
        using var cancellation = new CancellationTokenSource();
        Task<OperationExecution<DebitReceipt>>? pending = null;
        try
        {
            pending = service.ExecuteAsync<DebitInput, DebitReceipt>(contender.Context, PostgresOperationFixture.Request(),
                (_, _, _) => throw new InvalidOperationException("The contender never owns the claim."), cancellation.Token);
            await PostgresOperationFixture.ConfirmBlockedAsync(observer, contenderPid, winnerPid);
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
            Assert.IsTrue(pending.IsCompleted);
            Assert.IsTrue(contender.Context.IsRollbackOnly);
            TransactionCompletion completion = await contender.CommitAsync();
            Assert.AreEqual(TransactionOutcome.NotCommitted, completion.Outcome);
            Assert.IsFalse(completion.CommitAttempted);
            AssertCommitted(await winner.CommitAsync());
            OperationLookup<DebitReceipt> found = await service.GetResultAsync<DebitReceipt>(new("Debit", "alpha", "key"));
            Assert.AreEqual(OperationLookupDisposition.Found, found.Disposition);
            Assert.AreEqual(first.Decision?.Value, found.Decision?.Value);
        }
        finally
        {
            cancellation.Cancel();
            await winner.DisposeAsync();
            await JoinDuringCleanupAsync(pending, cancellation.Token);
        }
    }

    [TestMethod]
    public async Task ClaimLockTimeoutBecomesContendedOnlyAfterTheNativeOwnerIsPoisoned()
    {
        await using PostgresOperationFixture fixture = await PostgresOperationFixture.CreateAsync();
        await using AsyncServiceScope scope = fixture.CreateScope();
        IDurableOperationService service = scope.ServiceProvider.GetRequiredService<IDurableOperationService>();
        await using ILocalTransaction winner = await fixture.Factory.BeginAsync("Ledger");
        await using ILocalTransaction contender = await fixture.Factory.BeginAsync("Ledger");
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        int winnerPid = await PostgresOperationFixture.PidAsync(winner.Context);
        int contenderPid = await PostgresOperationFixture.PidAsync(contender.Context);
        await service.ExecuteAsync<DebitInput, DebitReceipt>(winner.Context, PostgresOperationFixture.Request(), fixture.HandleAsync);
        // Fixture-only SQL selects a deterministic server lock failure. The production adapter never changes session settings.
        await contender.Context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            await using DbCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SET LOCAL lock_timeout = '2000ms'";
            await command.ExecuteNonQueryAsync(token);
        });
        using var cancellation = new CancellationTokenSource();
        Task<OperationExecution<DebitReceipt>>? pending = null;
        try
        {
            pending = service.ExecuteAsync<DebitInput, DebitReceipt>(contender.Context, PostgresOperationFixture.Request(),
                (_, _, _) => throw new InvalidOperationException("No claim ownership."), cancellation.Token);
            await PostgresOperationFixture.ConfirmBlockedAsync(observer, contenderPid, winnerPid);
            OperationExecution<DebitReceipt> result = await pending;
            Assert.AreEqual(OperationExecutionDisposition.Contended, result.Disposition);
            Assert.IsTrue(contender.Context.IsRollbackOnly);
            Assert.AreEqual("transaction.work_failed", contender.Context.RollbackReasonCode);
            Assert.AreEqual(TransactionOutcome.NotCommitted, (await contender.CommitAsync()).Outcome);
        }
        finally
        {
            cancellation.Cancel();
            await winner.DisposeAsync();
            await JoinDuringCleanupAsync(pending, cancellation.Token);
        }
    }

    [TestMethod]
    public async Task NativeCommandTimeoutIsAnOriginalFaultAndNeverAHandlerRetry()
    {
        await using PostgresOperationFixture fixture = await PostgresOperationFixture.CreateAsync(commandTimeout: 2);
        await using AsyncServiceScope scope = fixture.CreateScope();
        IDurableOperationService service = scope.ServiceProvider.GetRequiredService<IDurableOperationService>();
        await using ILocalTransaction winner = await fixture.Factory.BeginAsync("Ledger");
        await using ILocalTransaction contender = await fixture.Factory.BeginAsync("Ledger");
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        int winnerPid = await PostgresOperationFixture.PidAsync(winner.Context);
        int contenderPid = await PostgresOperationFixture.PidAsync(contender.Context);
        await service.ExecuteAsync<DebitInput, DebitReceipt>(winner.Context, PostgresOperationFixture.Request(), fixture.HandleAsync);
        using var cancellation = new CancellationTokenSource();
        Task<OperationExecution<DebitReceipt>>? pending = null;
        try
        {
            pending = service.ExecuteAsync<DebitInput, DebitReceipt>(contender.Context, PostgresOperationFixture.Request(),
                (_, _, _) => throw new InvalidOperationException("A timeout never grants the claim."), cancellation.Token);
            await PostgresOperationFixture.ConfirmBlockedAsync(observer, contenderPid, winnerPid);
            NpgsqlException error = await Assert.ThrowsAsync<NpgsqlException>(() => pending);
            Assert.IsInstanceOfType<TimeoutException>(error.InnerException);
            Assert.IsTrue(pending.IsCompleted);
            Assert.IsTrue(contender.Context.IsRollbackOnly);
            TransactionCompletion completion = await contender.CommitAsync();
            Assert.AreEqual(TransactionOutcome.NotCommitted, completion.Outcome);
            Assert.IsFalse(completion.CommitAttempted);
        }
        finally
        {
            cancellation.Cancel();
            await winner.DisposeAsync();
            await JoinDuringCleanupAsync(pending, cancellation.Token);
        }
    }

    [TestMethod]
    [DataRow(TransactionIsolation.RepeatableRead)]
    [DataRow(TransactionIsolation.Serializable)]
    public async Task StrongerIsolationIsRejectedBeforeClaimOrHandlerWithoutDowngrade(TransactionIsolation isolation)
    {
        await using PostgresOperationFixture fixture = await PostgresOperationFixture.CreateAsync();
        await using AsyncServiceScope scope = fixture.CreateScope();
        IDurableOperationService service = scope.ServiceProvider.GetRequiredService<IDurableOperationService>();
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Ledger", new LocalTransactionOptions { Isolation = isolation });
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => service.ExecuteAsync<DebitInput, DebitReceipt>(owner.Context,
            PostgresOperationFixture.Request(), (_, _, _) => throw new InvalidOperationException("No handler under stronger isolation.")));
        Assert.AreEqual(isolation, owner.Context.Isolation);
        Assert.IsFalse(owner.Context.IsRollbackOnly);
        string selected = await owner.Context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            await using DbCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT current_setting('transaction_isolation')";
            return (string)(await command.ExecuteScalarAsync(token))!;
        });
        Assert.AreEqual(isolation == TransactionIsolation.RepeatableRead ? "repeatable read" : "serializable", selected);
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        Assert.AreEqual(new OperationTableCounts(0, 0, 0), await fixture.CountsAsync(observer));
    }

    [TestMethod]
    public async Task AFinalizationLockFailurePropagatesAndRollsBackEarlierBusinessAndOutgoingWrites()
    {
        await using PostgresOperationFixture fixture = await PostgresOperationFixture.CreateAsync();
        await fixture.ExecuteAsync($"""
            CREATE FUNCTION {fixture.Qualified("reject_finalization")}() RETURNS trigger LANGUAGE plpgsql AS
            $failure$ BEGIN RAISE EXCEPTION 'controlled finalization failure' USING ERRCODE = '55P03'; END $failure$;
            CREATE TRIGGER reject_finalization BEFORE UPDATE ON {fixture.JournalTable}
                FOR EACH ROW EXECUTE FUNCTION {fixture.Qualified("reject_finalization")}();
            """);
        await using AsyncServiceScope scope = fixture.CreateScope();
        IDurableOperationService service = scope.ServiceProvider.GetRequiredService<IDurableOperationService>();
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Ledger");
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => service.ExecuteAsync<DebitInput, DebitReceipt>(owner.Context,
            PostgresOperationFixture.Request(), fixture.HandleAsync));
        Assert.AreEqual("55P03", error.SqlState, "Only a claim INSERT failure maps to Contended; terminal-write failures remain original faults.");
        Assert.IsTrue(owner.Context.IsRollbackOnly);
        TransactionCompletion completion = await owner.CommitAsync();
        Assert.AreEqual(TransactionOutcome.NotCommitted, completion.Outcome);
        Assert.IsFalse(completion.CommitAttempted);
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        Assert.AreEqual(new OperationTableCounts(0, 0, 0), await fixture.CountsAsync(observer));
    }

    [TestMethod]
    public async Task UnexpectedCommittedIncompleteRowBlocksExecutionAndCannotBeFinalizedByAnotherTransaction()
    {
        await using PostgresOperationFixture fixture = await PostgresOperationFixture.CreateAsync();
        var identity = new OperationIdentity(fixture.Application.Application, fixture.Application.Environment,
            "tenant-alpha", "Debit", "key");
        var fingerprint = new OperationFingerprint(new("request", 1), SHA256.HashData([1, 2, 3]));
        await using (ILocalTransaction first = await fixture.Factory.BeginAsync("Ledger"))
        {
            Assert.AreEqual(OperationClaimDisposition.Acquired,
                (await fixture.Journal.TryClaimAsync(first.Context, identity, fingerprint)).Disposition);
            Assert.AreEqual(OperationClaimDisposition.MissingAfterConflict,
                (await fixture.Journal.TryClaimAsync(first.Context, identity, fingerprint)).Disposition,
                "An adapter call cannot label its own uncommitted claim as a committed original row.");
            AssertCommitted(await first.CommitAsync());
        }
        await using (ILocalTransaction second = await fixture.Factory.BeginAsync("Ledger"))
        {
            OperationClaim retained = await fixture.Journal.TryClaimAsync(second.Context, identity, fingerprint);
            Assert.AreEqual(OperationClaimDisposition.Existing, retained.Disposition);
            Assert.IsFalse(retained.Existing!.IsFinalized);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Journal.WriteCompletedAsync(second.Context,
                identity, fingerprint, new(new("result", 1), OperationBusinessDecision.Succeeded, new byte[] { 7 }), new()));
            Assert.IsTrue(second.Context.IsRollbackOnly);
            Assert.AreEqual(TransactionOutcome.NotCommitted, (await second.CommitAsync()).Outcome);
        }
        await using ILocalTransaction reader = await fixture.Factory.BeginAsync("Ledger");
        OperationJournalRecord? original = await fixture.Journal.ReadAsync(reader.Context, identity);
        Assert.IsNotNull(original);
        Assert.IsFalse(original.IsFinalized);
        Assert.IsNull(original.Result);
        await reader.RollbackAsync();
        await using AsyncServiceScope scope = fixture.CreateScope();
        IDurableOperationService service = scope.ServiceProvider.GetRequiredService<IDurableOperationService>();
        Assert.AreEqual(OperationLookupDisposition.Unavailable,
            (await service.GetResultAsync<DebitReceipt>(new("Debit", "alpha", "key"))).Disposition);
        await using ILocalTransaction execution = await fixture.Factory.BeginAsync("Ledger");
        Assert.AreEqual(OperationExecutionDisposition.Incompatible,
            (await service.ExecuteAsync<DebitInput, DebitReceipt>(execution.Context, PostgresOperationFixture.Request(),
                (_, _, _) => throw new InvalidOperationException("An incomplete committed row is not an execution invitation."))).Disposition);
    }

    [TestMethod]
    public async Task FullMaximumByteIdentityFitsTheUncompressedUniqueIndexAndKeepsOrdinalKeysDistinct()
    {
        await using PostgresOperationFixture fixture = await PostgresOperationFixture.CreateAsync();
        await fixture.ExecuteAsync($"""
            ALTER TABLE {fixture.JournalTable} ALTER COLUMN application SET STORAGE PLAIN;
            ALTER TABLE {fixture.JournalTable} ALTER COLUMN environment SET STORAGE PLAIN;
            ALTER TABLE {fixture.JournalTable} ALTER COLUMN scope SET STORAGE PLAIN;
            """);
        string application = SupplementaryText(255, 1);
        string environment = SupplementaryText(255, 2);
        string trustedScope = SupplementaryText(64, 3);
        string name = "A" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32))[..63];
        string key = "K" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(64))[..127];
        var identity = new OperationIdentity(application, environment, trustedScope, name, key);
        Assert.AreEqual(2488, Encoding.UTF8.GetByteCount(application + environment + trustedScope + name + key));
        var fingerprint = new OperationFingerprint(new("maximum-request", 1), SHA256.HashData([4, 5, 6]));
        foreach (string ordinalKey in new[] { key, "k" + key[1..] })
        {
            var selected = new OperationIdentity(application, environment, trustedScope, name, ordinalKey);
            await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Ledger");
            Assert.AreEqual(OperationClaimDisposition.Acquired,
                (await fixture.Journal.TryClaimAsync(owner.Context, selected, fingerprint)).Disposition);
            await fixture.Journal.WriteCompletedAsync(owner.Context, selected, fingerprint,
                new(new("maximum-result", 1), OperationBusinessDecision.Succeeded, new byte[] { 9 }), new());
            AssertCommitted(await owner.CommitAsync());
        }
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        await using var size = new NpgsqlCommand($"""
            SELECT count(*), min(pg_column_size(application)), min(pg_column_size(environment)), min(pg_column_size(scope))
            FROM {fixture.JournalTable}
            """, observer);
        await using NpgsqlDataReader result = await size.ExecuteReaderAsync();
        Assert.IsTrue(await result.ReadAsync());
        Assert.AreEqual(2L, result.GetInt64(0));
        Assert.AreEqual(1024, result.GetInt32(1));
        Assert.AreEqual(1024, result.GetInt32(2));
        Assert.AreEqual(260, result.GetInt32(3));
    }

    [TestMethod]
    public async Task TrustedScopesStaySeparateAndAuthorizationStillPrecedesReplayAndLookup()
    {
        await using PostgresOperationFixture fixture = await PostgresOperationFixture.CreateAsync();
        await using AsyncServiceScope scope = fixture.CreateScope();
        IDurableOperationService service = scope.ServiceProvider.GetRequiredService<IDurableOperationService>();
        foreach (string selectedScope in new[] { "alpha", "beta" })
        {
            await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Ledger");
            Assert.AreEqual(OperationExecutionDisposition.Prepared, (await service.ExecuteAsync<DebitInput, DebitReceipt>(owner.Context,
                PostgresOperationFixture.Request(scope: selectedScope), fixture.HandleAsync)).Disposition);
            AssertCommitted(await owner.CommitAsync());
        }
        fixture.Authorizer.Allowed = false;
        await using ILocalTransaction deniedOwner = await fixture.Factory.BeginAsync("Ledger");
        Assert.AreEqual(OperationExecutionDisposition.Denied, (await service.ExecuteAsync<DebitInput, DebitReceipt>(deniedOwner.Context,
            PostgresOperationFixture.Request(), (_, _, _) => throw new InvalidOperationException("Authorization denied."))).Disposition);
        Assert.AreEqual(OperationLookupDisposition.Denied,
            (await service.GetResultAsync<DebitReceipt>(new("Debit", "alpha", "key"))).Disposition);
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        Assert.AreEqual(new OperationTableCounts(2, 2, 2), await fixture.CountsAsync(observer));
    }

    [TestMethod]
    public async Task EveryIdentityColumnParticipatesInExactCaseSensitiveDatabaseUniqueness()
    {
        await using PostgresOperationFixture fixture = await PostgresOperationFixture.CreateAsync();
        OperationIdentity[] identities =
        [
            new("Application", "Environment", "Scope", "Operation", "OpaqueKey"),
            new("application", "Environment", "Scope", "Operation", "OpaqueKey"),
            new("Application", "environment", "Scope", "Operation", "OpaqueKey"),
            new("Application", "Environment", "scope", "Operation", "OpaqueKey"),
            new("Application", "Environment", "Scope", "operation", "OpaqueKey"),
            new("Application", "Environment", "Scope", "Operation", "opaqueKey")
        ];
        var fingerprint = new OperationFingerprint(new("request", 1), SHA256.HashData(new byte[] { 9 }));
        foreach (OperationIdentity identity in identities)
        {
            await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Ledger");
            Assert.AreEqual(OperationClaimDisposition.Acquired,
                (await fixture.Journal.TryClaimAsync(owner.Context, identity, fingerprint)).Disposition);
            await fixture.Journal.WriteCompletedAsync(owner.Context, identity, fingerprint,
                new(new("result", 1), OperationBusinessDecision.Succeeded, new byte[] { 1 }), new());
            AssertCommitted(await owner.CommitAsync());
        }
        foreach (OperationIdentity identity in identities)
        {
            await using ILocalTransaction reader = await fixture.Factory.BeginAsync("Ledger");
            Assert.AreEqual(identity, (await fixture.Journal.ReadAsync(reader.Context, identity))?.Identity);
        }
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        Assert.AreEqual(new OperationTableCounts(6, 0, 0), await fixture.CountsAsync(observer));
    }

    [TestMethod]
    public async Task UnsupportedSchemaVersionPreventsClaimAndHandler()
    {
        await using PostgresOperationFixture fixture = await PostgresOperationFixture.CreateAsync();
        await fixture.ExecuteAsync($"UPDATE {fixture.Qualified("schema_version")} SET version = 2");
        await using AsyncServiceScope scope = fixture.CreateScope();
        IDurableOperationService service = scope.ServiceProvider.GetRequiredService<IDurableOperationService>();
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Ledger");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.ExecuteAsync<DebitInput, DebitReceipt>(owner.Context,
            PostgresOperationFixture.Request(), (_, _, _) => throw new InvalidOperationException("Unsupported schema cannot execute.")));
        Assert.IsTrue(owner.Context.IsRollbackOnly);
        await owner.RollbackAsync();
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        Assert.AreEqual(new OperationTableCounts(0, 0, 0), await fixture.CountsAsync(observer));
    }

    [TestMethod]
    public async Task ExplicitInstallationRejectsPreexistingUnloggedOrMalformedOwnedTables()
    {
        await using PostgresOperationFixture fixture = await PostgresOperationFixture.CreateAsync();
        await fixture.ExecuteAsync($"DROP TABLE {fixture.JournalTable}; CREATE UNLOGGED TABLE {fixture.JournalTable} (unrelated integer)");
        await using NpgsqlConnection connection = await fixture.Source.OpenConnectionAsync();
        await using var install = new NpgsqlCommand(PostgreSqlOperationSchema.GetInstallationSql(fixture.Schema), connection);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => install.ExecuteNonQueryAsync());
        Assert.AreEqual(PostgresErrorCodes.DuplicateTable, error.SqlState,
            "The one-time installer never accepts existing owned tables merely because a version value says one.");
    }

    private static string SupplementaryText(int count, int seed)
    {
        var random = new Random(seed);
        var text = new StringBuilder(count * 2);
        for (int index = 0; index < count; index++)
        {
            text.Append(char.ConvertFromUtf32(random.Next(0x10000, 0xE0000)));
        }
        return text.ToString();
    }

    private static async Task JoinDuringCleanupAsync(Task? pending, CancellationToken cleanupRequested)
    {
        if (pending is null)
        {
            return;
        }
        try
        {
            await pending;
        }
        catch (Exception) when (cleanupRequested.IsCancellationRequested)
        {
            // The test has already observed its result/failure, or is preserving its primary assertion.
            // Cleanup still joins actual native settlement before owners, schemas or sources are released.
        }
    }

    private static void AssertCommitted(TransactionCompletion completion)
    {
        Assert.AreEqual(TransactionOutcome.Committed, completion.Outcome);
        Assert.IsTrue(completion.CommitAttempted);
        Assert.IsFalse(completion.RollbackAttempted);
        Assert.IsNull(completion.Error);
        Assert.HasCount(0, completion.CleanupErrors);
    }
}

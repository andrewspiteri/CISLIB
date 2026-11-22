using System.Data.Common;
using CIS.Operations;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Operations;

[TestClass]
[DoNotParallelize]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
public sealed class PostgresOperationRetentionTests
{
    [TestMethod]
    public async Task LogicalExpiryAndComparisonPrecedenceApplyBeforeMaintenanceAndPoliciesRemainPerRow()
    {
        await using PostgresOperationFixture fixture = await PostgresOperationFixture.CreateAsync();
        SeededResult old = await SeedAsync(fixture, "old-policy", new(TimeSpan.FromTicks(1)));
        await ExpiredOnServerAsync(fixture, "old-policy");
        await using AsyncServiceScope scope = fixture.CreateScope();
        IDurableOperationService service = scope.ServiceProvider.GetRequiredService<IDurableOperationService>();
        Assert.AreEqual(OperationLookupDisposition.ResultExpired,
            (await service.GetResultAsync<DebitReceipt>(new("Debit", "alpha", "old-policy"))).Disposition);
        Assert.AreEqual(OperationExecutionDisposition.ResultExpired, await ExecuteWithoutHandlerAsync(fixture, service, "old-policy"));
        Assert.AreEqual(OperationExecutionDisposition.KeyConflict, await ExecuteWithoutHandlerAsync(fixture, service, "old-policy", amount: 11));

        // The current definition has indefinite defaults; an existing row's persisted policy does not change.
        await using (ILocalTransaction current = await fixture.Factory.BeginAsync("Ledger"))
        {
            Assert.AreEqual(OperationExecutionDisposition.Prepared, (await service.ExecuteAsync<DebitInput, DebitReceipt>(current.Context,
                PostgresOperationFixture.Request(key: "new-policy"), fixture.HandleAsync)).Disposition);
            AssertCommitted(await current.CommitAsync());
        }
        await fixture.ExecuteAsync($"UPDATE {fixture.JournalTable} SET fingerprint_version = 2 WHERE idempotency_key = 'old-policy'");
        Assert.AreEqual(OperationExecutionDisposition.Incompatible,
            await ExecuteWithoutHandlerAsync(fixture, service, "old-policy", amount: 11));
        OperationMaintenanceResult maintenance = await scope.ServiceProvider.GetRequiredService<IOperationRetentionMaintenance>()
            .RunAsync(new("Debit", "alpha"));
        Assert.AreEqual(1, maintenance.ConfirmedPayloadsCleared);
        Assert.AreEqual(0, maintenance.ConfirmedRowsDeleted);
        Assert.AreEqual(OperationExecutionDisposition.Incompatible, await ExecuteWithoutHandlerAsync(fixture, service, "old-policy"));
        Assert.AreEqual(OperationExecutionDisposition.Replayed, await ExecuteWithoutHandlerAsync(fixture, service, "new-policy"));
        await using ILocalTransaction reader = await fixture.Factory.BeginAsync("Ledger");
        OperationJournalRecord? row = await fixture.Journal.ReadAsync(reader.Context, Identity(fixture, "old-policy"));
        Assert.IsNotNull(row);
        Assert.AreEqual(old.Times.ResultExpiresAtUtc, row.Times.ResultExpiresAtUtc);
        Assert.IsNull(row.Times.ProtectionExpiresAtUtc);
        Assert.IsNull(row.Result, "A tombstone keeps identity and fingerprint after payload clearing.");
        Assert.IsNotNull(row.Fingerprint);
    }

    [TestMethod]
    public async Task ProtectionEligibilityAloneDoesNotReleaseAKeyButActualPrivilegedDeletionDoes()
    {
        await using PostgresOperationFixture fixture = await PostgresOperationFixture.CreateAsync();
        await SeedAsync(fixture, "key", new(TimeSpan.FromTicks(1), TimeSpan.FromTicks(1)));
        await ExpiredOnServerAsync(fixture, "key");
        await using AsyncServiceScope scope = fixture.CreateScope();
        IDurableOperationService service = scope.ServiceProvider.GetRequiredService<IDurableOperationService>();
        Assert.AreEqual(OperationExecutionDisposition.ResultExpired, await ExecuteWithoutHandlerAsync(fixture, service, "key"));
        Assert.AreEqual(OperationExecutionDisposition.KeyConflict, await ExecuteWithoutHandlerAsync(fixture, service, "key", 11));
        OperationMaintenanceResult result = await scope.ServiceProvider.GetRequiredService<IOperationRetentionMaintenance>().RunAsync(new("Debit", "alpha"));
        Assert.AreEqual(1, result.ConfirmedRowsDeleted);
        Assert.AreEqual(0, result.ConfirmedPayloadsCleared);
        await using ILocalTransaction fresh = await fixture.Factory.BeginAsync("Ledger");
        Assert.AreEqual(OperationExecutionDisposition.Prepared, (await service.ExecuteAsync<DebitInput, DebitReceipt>(fresh.Context,
            PostgresOperationFixture.Request(amount: 11), fixture.HandleAsync)).Disposition);
        AssertCommitted(await fresh.CommitAsync());
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        Assert.AreEqual(new OperationTableCounts(1, 1, 1), await fixture.CountsAsync(observer));
    }

    [TestMethod]
    public async Task UnsupportedResultVersionsAndDecodeFailuresKeepIndefiniteDuplicateProtection()
    {
        await using PostgresOperationFixture fixture = await PostgresOperationFixture.CreateAsync();
        await SeedAsync(fixture, "key", new());
        await using AsyncServiceScope scope = fixture.CreateScope();
        IDurableOperationService service = scope.ServiceProvider.GetRequiredService<IDurableOperationService>();
        await fixture.ExecuteAsync($"UPDATE {fixture.JournalTable} SET result_version = 2");
        Assert.AreEqual(OperationLookupDisposition.Incompatible,
            (await service.GetResultAsync<DebitReceipt>(new("Debit", "alpha", "key"))).Disposition);
        Assert.AreEqual(OperationExecutionDisposition.Incompatible, await ExecuteWithoutHandlerAsync(fixture, service, "key"));
        await fixture.ExecuteAsync($"UPDATE {fixture.JournalTable} SET result_version = 1, result_payload = decode('00','hex')");
        Assert.AreEqual(OperationExecutionDisposition.Incompatible, await ExecuteWithoutHandlerAsync(fixture, service, "key"));
        Assert.AreEqual(OperationLookupDisposition.Incompatible,
            (await service.GetResultAsync<DebitReceipt>(new("Debit", "alpha", "key"))).Disposition);
        OperationMaintenanceResult maintenance = await scope.ServiceProvider.GetRequiredService<IOperationRetentionMaintenance>().RunAsync(new("Debit", "alpha"));
        Assert.AreEqual(0, maintenance.ConfirmedPayloadsCleared);
        Assert.AreEqual(0, maintenance.ConfirmedRowsDeleted);
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        Assert.AreEqual(new OperationTableCounts(1, 0, 0), await fixture.CountsAsync(observer));
    }

    [TestMethod]
    public async Task OneBatchLocksAtMost256RowsInDeterministicOrderAndNeverDeletesIndefiniteProtection()
    {
        await using PostgresOperationFixture fixture = await PostgresOperationFixture.CreateAsync();
        await using (ILocalTransaction owner = await fixture.Factory.BeginAsync("Ledger"))
        {
            for (int index = 0; index < 257; index++)
            {
                // Direct infrastructure seeding supplies historical rows; this is not257 coordinator attempts in one context.
                await StageSeedAsync(fixture, owner.Context, "batch-" + index.ToString("D4"), new(TimeSpan.FromTicks(1)));
            }
            AssertCommitted(await owner.CommitAsync());
        }
        await ExpiredOnServerAsync(fixture, "batch-0256");
        await using AsyncServiceScope scope = fixture.CreateScope();
        IOperationRetentionMaintenance maintenance = scope.ServiceProvider.GetRequiredService<IOperationRetentionMaintenance>();
        OperationMaintenanceResult first = await maintenance.RunAsync(new("Debit", "alpha"));
        Assert.AreEqual(256, first.ConfirmedPayloadsCleared);
        Assert.AreEqual(0, first.ConfirmedRowsDeleted);
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        await using (var pending = new NpgsqlCommand($"SELECT idempotency_key FROM {fixture.JournalTable} WHERE result_payload IS NOT NULL", observer))
        {
            Assert.AreEqual("batch-0256", await pending.ExecuteScalarAsync());
        }
        OperationMaintenanceResult second = await maintenance.RunAsync(new("Debit", "alpha", maximumRows: 1));
        Assert.AreEqual(1, second.ConfirmedPayloadsCleared);
        Assert.AreEqual(0, second.ConfirmedRowsDeleted);
        Assert.AreEqual(new OperationTableCounts(257, 0, 0), await fixture.CountsAsync(observer));
    }

    [TestMethod]
    public async Task SkipLockedMaintenanceDoesNotBlockLookupOrRemoveLockedProtection()
    {
        await using PostgresOperationFixture fixture = await PostgresOperationFixture.CreateAsync();
        await SeedAsync(fixture, "a-locked", new(TimeSpan.FromTicks(1)));
        await SeedAsync(fixture, "b-free", new(TimeSpan.FromTicks(1)));
        await ExpiredOnServerAsync(fixture, "b-free");
        await using ILocalTransaction blocker = await fixture.Factory.BeginAsync("Ledger");
        await blocker.Context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            await using DbCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"SELECT idempotency_key FROM {fixture.JournalTable} WHERE idempotency_key = 'a-locked' FOR UPDATE";
            Assert.AreEqual("a-locked", await command.ExecuteScalarAsync(token));
        });
        await using AsyncServiceScope scope = fixture.CreateScope();
        IDurableOperationService service = scope.ServiceProvider.GetRequiredService<IDurableOperationService>();
        IOperationRetentionMaintenance maintenance = scope.ServiceProvider.GetRequiredService<IOperationRetentionMaintenance>();
        OperationMaintenanceResult first = await maintenance.RunAsync(new("Debit", "alpha"));
        Assert.AreEqual(1, first.ConfirmedPayloadsCleared);
        Assert.AreEqual(0, first.ConfirmedRowsDeleted);
        Assert.AreEqual(OperationLookupDisposition.ResultExpired,
            (await service.GetResultAsync<DebitReceipt>(new("Debit", "alpha", "a-locked"))).Disposition);
        Assert.AreEqual(OperationExecutionDisposition.ResultExpired, await ExecuteWithoutHandlerAsync(fixture, service, "a-locked"));
        await blocker.RollbackAsync();
        Assert.AreEqual(1, (await maintenance.RunAsync(new("Debit", "alpha"))).ConfirmedPayloadsCleared);
    }

    [TestMethod]
    public async Task UncommittedMaintenanceDeletionStillProtectsLookupAndArbitratesTheNextExecution()
    {
        await using PostgresOperationFixture fixture = await PostgresOperationFixture.CreateAsync();
        await SeedAsync(fixture, "key", new(TimeSpan.FromTicks(1), TimeSpan.FromTicks(1)));
        await ExpiredOnServerAsync(fixture, "key");
        long advisory = Random.Shared.NextInt64(1, long.MaxValue);
        // Pause after the tuple has its uncommitted deletion marker. A BEFORE DELETE hold
        // leaves the committed tuple intact, so ON CONFLICT may return its retained result without waiting.
        await fixture.ExecuteAsync($"""
            CREATE FUNCTION {fixture.Qualified("hold_delete")}() RETURNS trigger LANGUAGE plpgsql AS
            $hold$ BEGIN PERFORM pg_advisory_xact_lock({advisory}); RETURN NULL; END $hold$;
            CREATE TRIGGER hold_delete AFTER DELETE ON {fixture.JournalTable}
                FOR EACH ROW EXECUTE FUNCTION {fixture.Qualified("hold_delete")}();
            """);
        await using AsyncServiceScope scope = fixture.CreateScope();
        IDurableOperationService service = scope.ServiceProvider.GetRequiredService<IDurableOperationService>();
        IOperationRetentionMaintenance maintenance = scope.ServiceProvider.GetRequiredService<IOperationRetentionMaintenance>();
        await using NpgsqlConnection blocker = await fixture.Source.OpenConnectionAsync();
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        await using ILocalTransaction contender = await fixture.Factory.BeginAsync("Ledger");
        int contenderPid = await PostgresOperationFixture.PidAsync(contender.Context);
        using var cancellation = new CancellationTokenSource();
        Task<OperationMaintenanceResult>? batch = null;
        Task<OperationExecution<DebitReceipt>>? execution = null;
        bool held = false;
        int handlers = 0;
        try
        {
            await AdvisoryAsync(blocker, advisory, acquire: true);
            held = true;
            batch = maintenance.RunAsync(new("Debit", "alpha"), cancellation.Token);
            int maintenancePid = await FindBlockedBackendAsync(observer, blocker.ProcessID);
            execution = service.ExecuteAsync<DebitInput, DebitReceipt>(contender.Context, PostgresOperationFixture.Request(),
                async (context, input, token) =>
                {
                    handlers++;
                    return await fixture.HandleAsync(context, input, token);
                }, cancellation.Token);
            await PostgresOperationFixture.ConfirmBlockedAsync(observer, contenderPid, maintenancePid);
            Assert.AreEqual(0, handlers);
            Assert.IsFalse(batch.IsCompleted);
            Assert.AreEqual(OperationLookupDisposition.ResultExpired,
                (await service.GetResultAsync<DebitReceipt>(new("Debit", "alpha", "key"))).Disposition);
            await AdvisoryAsync(blocker, advisory, acquire: false);
            held = false;
            Assert.AreEqual(1, (await batch).ConfirmedRowsDeleted);
            Assert.AreEqual(OperationExecutionDisposition.Prepared, (await execution).Disposition);
            Assert.AreEqual(1, handlers, "The new handler runs only after actual removal ended the old key's protection.");
            AssertCommitted(await contender.CommitAsync());
            Assert.AreEqual(new OperationTableCounts(1, 1, 1), await fixture.CountsAsync(observer));
        }
        finally
        {
            cancellation.Cancel();
            if (held)
            {
                await AdvisoryAsync(blocker, advisory, acquire: false);
            }
            await JoinAsync(batch, cancellation.Token);
            await JoinAsync(execution, cancellation.Token);
        }
    }

    [TestMethod]
    public async Task RealClaimDeadlockPoisonsOneOwnerAndRequiresExplicitFreshTransactionRetry()
    {
        await using PostgresOperationFixture fixture = await PostgresOperationFixture.CreateAsync();
        long firstLock = Random.Shared.NextInt64(1, long.MaxValue / 2);
        long secondLock = firstLock + 1;
        await fixture.ExecuteAsync($"""
            CREATE FUNCTION {fixture.Qualified("deadlock_claim")}() RETURNS trigger LANGUAGE plpgsql AS
            $deadlock$ BEGIN
                IF NEW.idempotency_key = 'deadlock-a' THEN PERFORM pg_advisory_xact_lock({secondLock});
                ELSIF NEW.idempotency_key = 'deadlock-b' THEN PERFORM pg_advisory_xact_lock({firstLock}); END IF;
                RETURN NEW;
            END $deadlock$;
            CREATE TRIGGER deadlock_claim BEFORE INSERT ON {fixture.JournalTable}
                FOR EACH ROW EXECUTE FUNCTION {fixture.Qualified("deadlock_claim")}();
            """);
        await using AsyncServiceScope firstScope = fixture.CreateScope();
        await using AsyncServiceScope secondScope = fixture.CreateScope();
        IDurableOperationService firstService = firstScope.ServiceProvider.GetRequiredService<IDurableOperationService>();
        IDurableOperationService secondService = secondScope.ServiceProvider.GetRequiredService<IDurableOperationService>();
        await using ILocalTransaction first = await fixture.Factory.BeginAsync("Ledger");
        await using ILocalTransaction second = await fixture.Factory.BeginAsync("Ledger");
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        long previousDeadlocks = await DeadlocksAsync(observer);
        int firstPid = await PostgresOperationFixture.PidAsync(first.Context);
        int secondPid = await PostgresOperationFixture.PidAsync(second.Context);
        await HoldTransactionLockAsync(first.Context, firstLock);
        await HoldTransactionLockAsync(second.Context, secondLock);
        using var cancellation = new CancellationTokenSource();
        Task<OperationExecution<DebitReceipt>>? a = null;
        Task<OperationExecution<DebitReceipt>>? b = null;
        int firstHandlers = 0;
        int secondHandlers = 0;
        try
        {
            a = firstService.ExecuteAsync<DebitInput, DebitReceipt>(first.Context, PostgresOperationFixture.Request(key: "deadlock-a"),
                async (context, input, token) =>
                {
                    firstHandlers++;
                    return await fixture.HandleAsync(context, input, token);
                }, cancellation.Token);
            await PostgresOperationFixture.ConfirmBlockedAsync(observer, firstPid, secondPid);
            b = secondService.ExecuteAsync<DebitInput, DebitReceipt>(second.Context, PostgresOperationFixture.Request(key: "deadlock-b"),
                async (context, input, token) =>
                {
                    secondHandlers++;
                    return await fixture.HandleAsync(context, input, token);
                }, cancellation.Token);
            await PostgresOperationFixture.ConfirmBlockedAsync(observer, secondPid, firstPid);
            Task<OperationExecution<DebitReceipt>> earliest = await Task.WhenAny(a, b);
            OperationExecutionDisposition earliestDisposition = (await earliest).Disposition;
            Task<OperationExecution<DebitReceipt>> victim;
            if (earliestDisposition == OperationExecutionDisposition.RetryRequired)
            {
                victim = earliest;
            }
            else
            {
                Assert.AreEqual(OperationExecutionDisposition.Prepared, earliestDisposition);
                victim = ReferenceEquals(earliest, a) ? b : a;
                Assert.AreEqual(OperationExecutionDisposition.RetryRequired, (await victim).Disposition);
            }
            bool firstLost = ReferenceEquals(victim, a);
            ILocalTransaction victimOwner = firstLost ? first : second;
            ILocalTransaction survivorOwner = firstLost ? second : first;
            Assert.IsTrue(victimOwner.Context.IsRollbackOnly);
            Assert.AreEqual(0, firstLost ? firstHandlers : secondHandlers);
            TransactionCompletion refused = await victimOwner.CommitAsync();
            Assert.AreEqual(TransactionOutcome.NotCommitted, refused.Outcome);
            Assert.IsFalse(refused.CommitAttempted);
            Assert.AreEqual(OperationExecutionDisposition.Prepared, (await (firstLost ? b : a)).Disposition);
            AssertCommitted(await survivorOwner.CommitAsync());
            await WaitForDeadlockAsync(observer, previousDeadlocks);
            await using ILocalTransaction retry = await fixture.Factory.BeginAsync("Ledger");
            Assert.AreEqual(OperationExecutionDisposition.Prepared, (await firstService.ExecuteAsync<DebitInput, DebitReceipt>(retry.Context,
                PostgresOperationFixture.Request(key: firstLost ? "deadlock-a" : "deadlock-b"), fixture.HandleAsync)).Disposition);
            AssertCommitted(await retry.CommitAsync());
            Assert.AreEqual(new OperationTableCounts(2, 2, 2), await fixture.CountsAsync(observer));
            Assert.AreEqual(1, firstHandlers + secondHandlers, "The library ran no hidden handler retry after the deadlock.");
        }
        finally
        {
            cancellation.Cancel();
            await JoinAsync(a, cancellation.Token);
            await JoinAsync(b, cancellation.Token);
        }
    }

    [TestMethod]
    public async Task SimulatedAcknowledgementLossAfterRealNativeCommitReportsUnknownAndFreshLookupRecoversOriginal()
    {
        await using PostgresOperationFixture fixture = await PostgresOperationFixture.CreateAsync(nativeCommitBoundary: true);
        NativeCommitBoundarySource boundary = fixture.CommitBoundary!;
        await using AsyncServiceScope scope = fixture.CreateScope();
        IDurableOperationService service = scope.ServiceProvider.GetRequiredService<IDurableOperationService>();
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Ledger");
        OperationExecution<DebitReceipt> prepared = await service.ExecuteAsync<DebitInput, DebitReceipt>(owner.Context,
            PostgresOperationFixture.Request(), fixture.HandleAsync);
        boundary.FailAfterNextCommit();
        TransactionCompletion unknown = await owner.CommitAsync();
        Assert.AreEqual(TransactionOutcome.Unknown, unknown.Outcome);
        Assert.IsTrue(unknown.CommitAttempted);
        Assert.IsTrue(unknown.RollbackAttempted);
        Assert.AreEqual(1, boundary.CommitInvocations);
        Assert.AreEqual(1, boundary.ConfirmedNativeCommits);
        Assert.AreEqual(1, boundary.RollbackInvocations);
        await owner.DisposeAsync();
        Assert.AreEqual(1, boundary.RollbackInvocations, "Disposing a terminal Unknown owner does not retry rollback or commit.");
        OperationLookup<DebitReceipt> found = await service.GetResultAsync<DebitReceipt>(new("Debit", "alpha", "key"));
        Assert.AreEqual(OperationLookupDisposition.Found, found.Disposition);
        Assert.AreEqual(prepared.Decision?.Value, found.Decision?.Value);
        Assert.AreEqual(TransactionOutcome.Unknown, unknown.Outcome, "Fresh recovery does not rewrite the original completion fact.");
        await using ILocalTransaction retry = await fixture.Factory.BeginAsync("Ledger");
        Assert.AreEqual(OperationExecutionDisposition.Replayed, (await service.ExecuteAsync<DebitInput, DebitReceipt>(retry.Context,
            PostgresOperationFixture.Request(), (_, _, _) => throw new InvalidOperationException("No second handler after recovered commit."))).Disposition);
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        Assert.AreEqual(new OperationTableCounts(1, 1, 1), await fixture.CountsAsync(observer));
    }

    [TestMethod]
    public async Task ConfirmedNativeCommitRemainsCommittedWhenActualTransactionDisposalReportsFailure()
    {
        await using PostgresOperationFixture fixture = await PostgresOperationFixture.CreateAsync(nativeCommitBoundary: true);
        await using AsyncServiceScope scope = fixture.CreateScope();
        IDurableOperationService service = scope.ServiceProvider.GetRequiredService<IDurableOperationService>();
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Ledger");
        OperationExecution<DebitReceipt> prepared = await service.ExecuteAsync<DebitInput, DebitReceipt>(owner.Context,
            PostgresOperationFixture.Request(), fixture.HandleAsync);
        fixture.CommitBoundary!.FailAfterNextTransactionDisposal();
        TransactionCompletion completion = await owner.CommitAsync();
        Assert.AreEqual(TransactionOutcome.Committed, completion.Outcome);
        Assert.IsFalse(completion.RollbackAttempted);
        Assert.HasCount(1, completion.CleanupErrors);
        Assert.AreEqual(TransactionErrorPhase.TransactionDispose, completion.CleanupErrors[0].Phase);
        Assert.AreEqual(1, fixture.CommitBoundary.ConfirmedNativeCommits);
        Assert.AreEqual(1, fixture.CommitBoundary.ConnectionDisposals);
        Assert.AreEqual(prepared.Decision?.Value,
            (await service.GetResultAsync<DebitReceipt>(new("Debit", "alpha", "key"))).Decision?.Value);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task MaintenanceCountsStayCandidatesAfterUnknownAndConfirmedAfterCommittedCleanupFailure(bool acknowledgementLost)
    {
        await using PostgresOperationFixture fixture = await PostgresOperationFixture.CreateAsync(nativeCommitBoundary: true);
        await SeedAsync(fixture, "key", new(TimeSpan.FromTicks(1), TimeSpan.FromTicks(1)));
        await ExpiredOnServerAsync(fixture, "key");
        if (acknowledgementLost)
        {
            fixture.CommitBoundary!.FailAfterNextCommit();
        }
        else
        {
            fixture.CommitBoundary!.FailAfterNextTransactionDisposal();
        }
        await using AsyncServiceScope scope = fixture.CreateScope();
        OperationMaintenanceResult result = await scope.ServiceProvider.GetRequiredService<IOperationRetentionMaintenance>().RunAsync(new("Debit", "alpha"));
        Assert.AreEqual(OperationMaintenanceDisposition.Executed, result.Disposition);
        Assert.AreEqual(1, result.CandidateRowsDeleted);
        Assert.AreEqual(acknowledgementLost ? TransactionOutcome.Unknown : TransactionOutcome.Committed, result.Completion?.Outcome);
        Assert.AreEqual(acknowledgementLost ? null : (int?)1, result.ConfirmedRowsDeleted);
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        Assert.AreEqual(new OperationTableCounts(0, 0, 0), await fixture.CountsAsync(observer), "The separate observer sees the real commit, without fabricating a confirmed call result.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DisabledAndDeniedMaintenanceDiscloseNoCountsAndLeaveRowsUntouched(bool enabled)
    {
        await using PostgresOperationFixture fixture = await PostgresOperationFixture.CreateAsync(maintenanceEnabled: enabled);
        await SeedAsync(fixture, "key", new(TimeSpan.FromTicks(1)));
        fixture.Authorizer.Allowed = false;
        await using AsyncServiceScope scope = fixture.CreateScope();
        OperationMaintenanceResult result = await scope.ServiceProvider.GetRequiredService<IOperationRetentionMaintenance>().RunAsync(new("Debit", "alpha"));
        Assert.AreEqual(enabled ? OperationMaintenanceDisposition.Denied : OperationMaintenanceDisposition.Disabled, result.Disposition);
        Assert.IsNull(result.CandidatePayloadsCleared);
        Assert.IsNull(result.CandidateRowsDeleted);
        Assert.IsNull(result.Completion);
        Assert.IsNull(result.ConfirmedRowsDeleted);
        Assert.AreEqual(enabled ? 1 : 0, fixture.Authorizer.Calls);
        if (enabled)
        {
            Assert.AreEqual("account-alpha", fixture.Authorizer.Target?.ResourceIdentifier);
            Assert.AreEqual(fixture.Application.Application, fixture.Authorizer.Target?.Application);
            CollectionAssert.AreEqual(new[] { "debit.maintain" }, fixture.Authorizer.Capabilities!.ToArray());
        }
        await using NpgsqlConnection observer = await fixture.Source.OpenConnectionAsync();
        await using var payload = new NpgsqlCommand($"SELECT octet_length(result_payload) FROM {fixture.JournalTable}", observer);
        Assert.AreEqual(20, await payload.ExecuteScalarAsync());
    }

    [TestMethod]
    public async Task PositiveOneTickRetentionRoundsUpToNativeMicrosecondsRatherThanZeroOrFailure()
    {
        await using PostgresOperationFixture fixture = await PostgresOperationFixture.CreateAsync();
        SeededResult seeded = await SeedAsync(fixture, "key", new(TimeSpan.FromTicks(1), TimeSpan.FromTicks(1)));
        DateTimeOffset recorded = seeded.Times.RecordedAtUtc ?? throw new AssertFailedException("The result must have a native recording time.");
        DateTimeOffset expires = seeded.Times.ResultExpiresAtUtc ?? throw new AssertFailedException("The finite policy must have a native expiry time.");
        Assert.AreEqual(10L, (expires - recorded).Ticks);
        Assert.AreEqual(seeded.Times.ResultExpiresAtUtc, seeded.Times.ProtectionExpiresAtUtc);
    }

    private static OperationIdentity Identity(PostgresOperationFixture fixture, string key) =>
        new(fixture.Application.Application, fixture.Application.Environment, "tenant-alpha", "Debit", key);

    private static async Task<SeededResult> SeedAsync(PostgresOperationFixture fixture, string key, OperationRetentionPolicy policy)
    {
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Ledger");
        SeededResult result = await StageSeedAsync(fixture, owner.Context, key, policy);
        AssertCommitted(await owner.CommitAsync());
        return result;
    }

    private static async Task<SeededResult> StageSeedAsync(PostgresOperationFixture fixture, ILocalTransactionContext context,
        string key, OperationRetentionPolicy policy)
    {
        NormalizedOperationInput<DebitInput> normalized = new DebitNormalizer().Normalize(new("ignored", 10, true),
            new("tenant-alpha", "account", "account-alpha"));
        OperationFingerprint fingerprint = OperationCanonicalFrame.Fingerprint(normalized.Fingerprints.Single());
        OperationIdentity identity = Identity(fixture, key);
        Assert.AreEqual(OperationClaimDisposition.Acquired, (await fixture.Journal.TryClaimAsync(context, identity, fingerprint)).Disposition);
        var receipt = new DebitReceipt(Guid.NewGuid(), 10);
        OperationJournalTimes times = await fixture.Journal.WriteCompletedAsync(context, identity, fingerprint,
            new(fixture.Codec.CurrentContract, OperationBusinessDecision.Succeeded, fixture.Codec.Encode(receipt)), policy);
        return new(receipt, times);
    }

    private static async Task<OperationExecutionDisposition> ExecuteWithoutHandlerAsync(PostgresOperationFixture fixture,
        IDurableOperationService service, string key, int amount = 10)
    {
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Ledger");
        return (await service.ExecuteAsync<DebitInput, DebitReceipt>(owner.Context, PostgresOperationFixture.Request(amount, key),
            (_, _, _) => throw new InvalidOperationException("A retained key must not authorize protected reexecution."))).Disposition;
    }

    private static async Task ExpiredOnServerAsync(PostgresOperationFixture fixture, string key)
    {
        await using NpgsqlConnection connection = await fixture.Source.OpenConnectionAsync();
        await using var query = new NpgsqlCommand($"SELECT result_expires_at <= clock_timestamp() FROM {fixture.JournalTable} WHERE idempotency_key = @key", connection);
        query.Parameters.AddWithValue("key", key);
        Assert.AreEqual(true, await query.ExecuteScalarAsync(), "Native server time, rather than a test sleep, establishes logical expiry.");
    }

    private static Task HoldTransactionLockAsync(ILocalTransactionContext context, long key) => context.UseAdoNetAsync(async (connection, transaction, token) =>
    {
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT pg_advisory_xact_lock(@key)";
        PostgresOperationFixture.Add(command, "key", key);
        await command.ExecuteNonQueryAsync(token);
    });

    private static async Task AdvisoryAsync(NpgsqlConnection connection, long key, bool acquire)
    {
        await using var command = new NpgsqlCommand(acquire ? "SELECT pg_advisory_lock(@key)" : "SELECT pg_advisory_unlock(@key)", connection);
        command.Parameters.AddWithValue("key", key);
        object? value = await command.ExecuteScalarAsync();
        if (!acquire)
        {
            Assert.AreEqual(true, value);
        }
    }

    private static async Task<int> FindBlockedBackendAsync(NpgsqlConnection observer, int blocker)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        for (int attempt = 0; attempt < 512; attempt++)
        {
            await using var command = new NpgsqlCommand("SELECT pid FROM pg_stat_activity WHERE datname = current_database() AND @blocker = ANY(pg_blocking_pids(pid))", observer);
            command.Parameters.AddWithValue("blocker", blocker);
            object? pid = await command.ExecuteScalarAsync(cancellation.Token);
            if (pid is int found)
            {
                return found;
            }
            await Task.Delay(10, cancellation.Token);
        }
        throw new AssertFailedException("The server did not observe the owned maintenance blocker.");
    }

    private static async Task<long> DeadlocksAsync(NpgsqlConnection observer)
    {
        await using var command = new NpgsqlCommand("SELECT deadlocks FROM pg_stat_database WHERE datname = current_database()", observer);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task WaitForDeadlockAsync(NpgsqlConnection observer, long previous)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        for (int attempt = 0; attempt < 512; attempt++)
        {
            if (await DeadlocksAsync(observer) > previous)
            {
                return;
            }
            await Task.Delay(20, cancellation.Token);
        }
        Assert.Fail("The server did not publish its actual deadlock observation.");
    }

    private static async Task JoinAsync(Task? task, CancellationToken cleanup)
    {
        if (task is null)
        {
            return;
        }
        try
        {
            await task;
        }
        catch (Exception) when (cleanup.IsCancellationRequested)
        {
            // Preserve the primary test outcome while joining every native task before owner/source/schema release.
        }
    }

    private static void AssertCommitted(TransactionCompletion completion)
    {
        Assert.AreEqual(TransactionOutcome.Committed, completion.Outcome);
        Assert.IsNull(completion.Error);
        Assert.HasCount(0, completion.CleanupErrors);
    }

    private sealed record SeededResult(DebitReceipt Receipt, OperationJournalTimes Times);
}

using CIS.Operations;
using CIS.Tests.IntegrationTests.Infrastructure.Containers;
using CIS.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Operations;

[TestClass]
[DoNotParallelize]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
public sealed class PostgresOperationRestartTests
{
    [TestMethod]
    public async Task FailedReconstructionPreservesThePreviousOwnersSchemaAndCommittedResult()
    {
        await using PostgresOperationFixture original = await PostgresOperationFixture.CreateAsync();
        DebitReceipt committed;
        await using (AsyncServiceScope scope = original.CreateScope())
        await using (ILocalTransaction owner = await original.Factory.BeginAsync("Ledger"))
        {
            IDurableOperationService service = scope.ServiceProvider.GetRequiredService<IDurableOperationService>();
            OperationExecution<DebitReceipt> result = await service.ExecuteAsync<DebitInput, DebitReceipt>(owner.Context,
                PostgresOperationFixture.Request(key: "retained"), original.HandleAsync);
            Assert.IsNotNull(result.Decision);
            Assert.IsNotNull(result.Decision.Value);
            committed = result.Decision.Value;
            Assert.AreEqual(TransactionOutcome.Committed, (await owner.CommitAsync()).Outcome);
        }

        // An unexpected ordinary table makes real reconstruction validation fail after borrowing the schema.
        await original.ExecuteAsync($"CREATE TABLE {original.Qualified("unexpected")} (value integer NOT NULL)");
        await Assert.ThrowsExactlyAsync<AssertFailedException>(() => PostgresOperationFixture.ReopenAsync(original));
        await original.ExecuteAsync($"DROP TABLE {original.Qualified("unexpected")}");
        await using (NpgsqlConnection observer = await original.Source.OpenConnectionAsync())
        {
            Assert.AreEqual(new OperationTableCounts(1, 1, 1), await original.CountsAsync(observer));
        }

        await using PostgresOperationFixture recovered = await PostgresOperationFixture.ReopenAsync(original);
        await original.DisposeAsync();
        await using AsyncServiceScope recoveredScope = recovered.CreateScope();
        IDurableOperationService recoveredService = recoveredScope.ServiceProvider.GetRequiredService<IDurableOperationService>();
        OperationLookup<DebitReceipt> found = await recoveredService.GetResultAsync<DebitReceipt>(new("Debit", "alpha", "retained"));
        Assert.AreEqual(OperationLookupDisposition.Found, found.Disposition);
        Assert.AreEqual(committed, found.Decision?.Value);
    }

    [TestMethod]
    public async Task CleanSameStorageRestartRecoversTheOriginalResultAndLeavesNoUnfinishedEffects()
    {
        await using OwnedPostgresService database = await OwnedPostgresService.CreateDedicatedAsync();
        await using PostgresOperationFixture original = await PostgresOperationFixture.CreateAsync(database: database);
        DebitReceipt committed;
        await using (AsyncServiceScope scope = original.CreateScope())
        await using (ILocalTransaction owner = await original.Factory.BeginAsync("Ledger"))
        {
            IDurableOperationService service = scope.ServiceProvider.GetRequiredService<IDurableOperationService>();
            OperationExecution<DebitReceipt> result = await service.ExecuteAsync<DebitInput, DebitReceipt>(owner.Context,
                PostgresOperationFixture.Request(key: "committed"), original.HandleAsync);
            Assert.AreEqual(OperationExecutionDisposition.Prepared, result.Disposition);
            Assert.IsNotNull(result.Decision);
            Assert.IsNotNull(result.Decision.Value);
            committed = result.Decision.Value;
            Assert.AreEqual(TransactionOutcome.Committed, (await owner.CommitAsync()).Outcome);
        }
        await using AsyncServiceScope unfinishedScope = original.CreateScope();
        ILocalTransaction unfinished = await original.Factory.BeginAsync("Ledger");
        try
        {
            IDurableOperationService service = unfinishedScope.ServiceProvider.GetRequiredService<IDurableOperationService>();
            OperationExecution<DebitReceipt> staged = await service.ExecuteAsync<DebitInput, DebitReceipt>(unfinished.Context,
                PostgresOperationFixture.Request(key: "unfinished"), original.HandleAsync);
            Assert.AreEqual(OperationExecutionDisposition.Prepared, staged.Disposition);
            Assert.IsNull(unfinished.Completion);
            string? container = database.ContainerId;
            string? volume = database.VolumeName;
            using var restart = new CancellationTokenSource(IntegrationServices.StartupTimeout);
            // This is a clean owned-container stop/start, not power loss or a simulated commit acknowledgement.
            await database.Container.StopAsync(restart.Token);
            await unfinished.DisposeAsync();
            await unfinishedScope.DisposeAsync();
            await database.Container.StartAsync(restart.Token);
            await database.ValidateReadyAsync(restart.Token);
            Assert.AreEqual(container, database.Container.Id);
            Assert.AreEqual(volume, database.VolumeName);
        }
        finally
        {
            await unfinished.DisposeAsync();
        }

        // This creates a new data source, provider, authorizer, journal and codec over the retained schema.
        await using PostgresOperationFixture recovered = await PostgresOperationFixture.ReopenAsync(original);
        await original.DisposeAsync();
        await using AsyncServiceScope freshScope = recovered.CreateScope();
        IDurableOperationService fresh = freshScope.ServiceProvider.GetRequiredService<IDurableOperationService>();
        OperationLookup<DebitReceipt> found = await fresh.GetResultAsync<DebitReceipt>(new("Debit", "alpha", "committed"));
        Assert.AreEqual(OperationLookupDisposition.Found, found.Disposition);
        Assert.AreEqual(committed, found.Decision?.Value);
        Assert.AreEqual(OperationLookupDisposition.NotFound,
            (await fresh.GetResultAsync<DebitReceipt>(new("Debit", "alpha", "unfinished"))).Disposition);
        await using NpgsqlConnection observer = await recovered.Source.OpenConnectionAsync();
        Assert.AreEqual(new OperationTableCounts(1, 1, 1), await recovered.CountsAsync(observer));
        await using ILocalTransaction replayOwner = await recovered.Factory.BeginAsync("Ledger");
        OperationExecution<DebitReceipt> replay = await fresh.ExecuteAsync<DebitInput, DebitReceipt>(replayOwner.Context,
            PostgresOperationFixture.Request(key: "committed"), (_, _, _) => throw new InvalidOperationException("The recovered original result must not execute another business effect."));
        Assert.AreEqual(OperationExecutionDisposition.Replayed, replay.Disposition);
        Assert.AreEqual(committed, replay.Decision?.Value);
        Assert.AreEqual(TransactionOutcome.NotCommitted, (await replayOwner.RollbackAsync()).Outcome);
        Assert.AreEqual(new OperationTableCounts(1, 1, 1), await recovered.CountsAsync(observer));
    }
}

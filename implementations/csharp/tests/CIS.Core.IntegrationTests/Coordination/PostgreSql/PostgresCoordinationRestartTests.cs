using CIS.Coordination;
using CIS.Tests.IntegrationTests.Infrastructure.Containers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Coordination.PostgreSql;

[TestClass]
[DoNotParallelize]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
public sealed class PostgresCoordinationRestartTests
{
    [TestMethod]
    public async Task CleanSameStorageRestartRetainsAuthorityAndSequenceAfterReleaseAndPruning()
    {
        await using OwnedPostgresService database = await OwnedPostgresService.CreateDedicatedAsync();
        await using PostgresCoordinationFixture original = await PostgresCoordinationFixture.CreateAsync(database);
        CoordinationGrant first = await original.AcquireAsync();
        Assert.AreEqual(CoordinationMutationOutcome.Succeeded, (await original.Authority.ReleaseAsync(first)).Outcome);
        Assert.AreEqual(CoordinationMutationOutcome.Succeeded, (await original.Authority.PruneAsync(first)).Outcome);
        CoordinationGrant second = await original.AcquireAsync();
        Assert.IsGreaterThan(first.Generation, second.Generation);
        Assert.AreEqual(CoordinationMutationOutcome.Succeeded, (await original.Authority.ReleaseAsync(second)).Outcome);
        Assert.AreEqual(CoordinationMutationOutcome.Succeeded, (await original.Authority.PruneAsync(second)).Outcome);
        string? container = database.ContainerId;
        string? volume = database.VolumeName;
        original.Source.Clear();
        using var restart = new CancellationTokenSource(IntegrationServices.StartupTimeout);
        // This is an actual clean stop/start of the same owned container and persisted volume, not power-loss proof.
        await database.Container.StopAsync(restart.Token);
        await database.Container.StartAsync(restart.Token);
        await database.ValidateReadyAsync(restart.Token);
        Assert.AreEqual(container, database.Container.Id);
        Assert.AreEqual(volume, database.VolumeName);
        await using PostgresCoordinationFixture recovered = await PostgresCoordinationFixture.ReopenAsync(original, database);
        await original.DisposeAsync();
        Assert.AreEqual(original.AuthorityId, recovered.Authority.AuthorityId);
        CoordinationGrant third = await recovered.AcquireAsync();
        Assert.IsGreaterThan(second.Generation, third.Generation);
        Assert.AreEqual(CoordinationRejectionReason.Superseded, (await recovered.Authority.ReleaseAsync(second)).RejectionReason);
        Assert.AreEqual(CoordinationObservationOutcome.FoundLive, (await recovered.Authority.ResolveAsync(third.Acquisition)).Outcome);
    }
}

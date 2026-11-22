using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Infrastructure.Containers;

[TestClass]
[DoNotParallelize]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
public sealed class PostgresContainerLifecycleTests
{
    [TestMethod]
    public async Task ConcurrentAcquisitionSharesOneLazyServiceAndIndependentContainersKeepTheirOwnData()
    {
        OwnedPostgresService[] services = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => IntegrationServices.GetPostgresAsync()));
        Assert.IsTrue(services.All(service => ReferenceEquals(services[0], service)));
        OwnedPostgresService shared = services[0];
        await using OwnedPostgresService dedicated = await OwnedPostgresService.CreateDedicatedAsync();
        Assert.AreNotEqual(shared.ContainerId, dedicated.ContainerId);
        Assert.AreNotEqual(shared.VolumeName, dedicated.VolumeName);
        var sharedOptions = new NpgsqlConnectionStringBuilder(shared.CreateConnectionString(1));
        var dedicatedOptions = new NpgsqlConnectionStringBuilder(dedicated.CreateConnectionString(1));
        Assert.AreNotEqual(sharedOptions.Password, dedicatedOptions.Password);
        using IDisposable sharedLease = shared.AcquireClientLease();
        using IDisposable dedicatedLease = dedicated.AcquireClientLease();
        await using NpgsqlDataSource sharedSource = NpgsqlDataSource.Create(sharedOptions.ConnectionString);
        await using NpgsqlDataSource dedicatedSource = NpgsqlDataSource.Create(dedicatedOptions.ConnectionString);
        await using NpgsqlConnection first = await sharedSource.OpenConnectionAsync();
        await using NpgsqlConnection second = await dedicatedSource.OpenConnectionAsync();
        string schema = "cis_container_" + Guid.NewGuid().ToString("N");
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"; CREATE TABLE \"{schema}\".owned_record (value integer NOT NULL); INSERT INTO \"{schema}\".owned_record VALUES (7)", first))
        {
            await create.ExecuteNonQueryAsync();
        }
        try
        {
            await using var inspect = new NpgsqlCommand("SELECT count(*) FROM pg_namespace WHERE nspname=@schema", second);
            inspect.Parameters.AddWithValue("schema", schema);
            Assert.AreEqual(0L, await inspect.ExecuteScalarAsync(), "The independently owned container cannot see the shared service's schema.");
        }
        finally
        {
            await using var remove = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", first);
            await remove.ExecuteNonQueryAsync();
        }
    }

    [TestMethod]
    public async Task ActiveClientOwnershipPreventsContainerDeletionAndActualDisposalRemovesItsVolume()
    {
        OwnedPostgresService service = await OwnedPostgresService.CreateDedicatedAsync();
        try
        {
            IDisposable client = service.AcquireClientLease();
            try
            {
                await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.DisposeAsync().AsTask());
                await service.ValidateReadyAsync();
            }
            finally
            {
                client.Dispose();
            }
        }
        finally
        {
            await service.DisposeAsync();
        }
        await IntegrationServices.VerifyAbsentAsync(service.ContainerId, service.VolumeName, service.OwnerId);
        await service.DisposeAsync();
    }

    [TestMethod]
    public async Task FailedStartupJoinsCleanupAndReturnsTheDedicatedSlotForTheNextService()
    {
        await using OwnedPostgresService failed = await OwnedPostgresService.CreateFailedStartupProbeAsync();
        Exception? observed = null;
        try
        {
            await failed.StartAsync();
        }
        catch (Exception error)
        {
            observed = error;
        }
        Assert.IsNotNull(observed, "The deliberately invalid PostgreSQL setting must fail startup, not hide the selected case.");
        Assert.IsNotNull(failed.ContainerId, "This failure must occur after native container creation.");
        Assert.IsNotNull(failed.VolumeName);
        await IntegrationServices.VerifyAbsentAsync(failed.ContainerId, failed.VolumeName, failed.OwnerId);
        await failed.DisposeAsync();
        await using OwnedPostgresService subsequent = await OwnedPostgresService.CreateDedicatedAsync();
        await subsequent.ValidateReadyAsync();
        Assert.AreNotEqual(failed.ContainerId, subsequent.ContainerId);
    }
}

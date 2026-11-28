using System.Data.Common;
using CIS.Management.Service.Persistence;
using CIS.Tests.IntegrationTests.Infrastructure.Containers;
using CIS.Tests.IntegrationTests.Operations;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using CIS.Transactions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Management;

// Two independent RM-02 roots share the existing owned PostgreSQL service and one per-case schema.
internal sealed class ManagementDatabaseFixture : IAsyncDisposable
{
    private readonly IDisposable serviceLease;
    private readonly ServiceProvider first;
    private readonly ServiceProvider second;

    private ManagementDatabaseFixture(NpgsqlDataSource source, IDisposable serviceLease, ManagementPersistenceOptions options)
    {
        Source = source;
        this.serviceLease = serviceLease;
        Options = options;
        Boundary = new(source);
        first = Provider(Boundary);
        second = Provider(Boundary);
        Store = new(Factory, options);
        Replica = new(second.GetRequiredService<ILocalTransactionFactory>(), options);
    }

    internal NpgsqlDataSource Source
    {
        get;
    }
    internal NativeCommitBoundarySource Boundary
    {
        get;
    }
    internal ManagementPersistenceOptions Options
    {
        get;
    }
    internal ILocalTransactionFactory Factory => first.GetRequiredService<ILocalTransactionFactory>();
    internal PostgreSqlManagementStore Store
    {
        get;
    }
    internal PostgreSqlManagementStore Replica
    {
        get;
    }
    internal string Table(string name) => $"\"{Options.Schema}\".\"{name}\"";

    internal static string DropRuleEngineTables(Func<string, string> table)
    {
        string[] names =
        [
            "rule_engine_counter_reset", "rule_engine_counter_execution", "rule_engine_counter",
            "rule_engine_counter_scope", "rule_engine_execution_event", "rule_engine_execution_rejection",
            "rule_engine_execution", "rule_engine_release_history", "rule_engine_deployment_release",
            "rule_engine_deployed_version", "rule_engine_version", "rule_engine_identity",
            "rule_engine_schema", "rule_engine_ruleset"
        ];
        return "DROP TABLE " + string.Join(", ", names.Select(table)) + ";";
    }

    internal static async Task<ManagementDatabaseFixture> CreateAsync(
        Func<string, ManagementPersistenceOptions>? configure = null)
    {
        OwnedPostgresService service = await IntegrationServices.GetPostgresAsync();
        IDisposable lease = service.AcquireClientLease();
        NpgsqlDataSource? source = null;
        ManagementDatabaseFixture? fixture = null;
        try
        {
            string schema = "cis_rm07_" + Guid.NewGuid().ToString("N");
            ManagementPersistenceOptions options = configure?.Invoke(schema) ?? new(schema);
            source = NpgsqlDataSource.Create(service.CreateConnectionString(6));
            fixture = new(source, lease, options);
            await using ILocalTransaction install = await fixture.Factory.BeginAsync("management");
            await ManagementSchema.InstallAsync(install.Context, schema, options);
            Assert.AreEqual(TransactionOutcome.Committed, (await install.CommitAsync()).Outcome);
            await fixture.ReadAsync(async (connection, transaction, token) =>
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "SELECT current_setting('server_version_num')::integer,current_setting('fsync'),current_setting('synchronous_commit'),current_setting('full_page_writes')";
                await using DbDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.IsTrue(reader.GetInt32(0) is >= 180000 and < 190000);
                Assert.AreEqual("on", reader.GetString(1));
                Assert.AreEqual("on", reader.GetString(2));
                Assert.AreEqual("on", reader.GetString(3));
                return 0;
            });
            return fixture;
        }
        catch (Exception original)
        {
            try
            {
                if (fixture is not null)
                {
                    await fixture.DisposeAsync();
                }
                else
                {
                    if (source is not null)
                    {
                        await source.DisposeAsync();
                    }
                    lease.Dispose();
                }
            }
            catch (Exception cleanup)
            {
                throw new AggregateException("Management fixture initialization and owned cleanup failed.", original, cleanup);
            }
            throw;
        }
    }

    internal async Task SqlAsync(string sql)
    {
        await using ILocalTransaction owner = await Factory.BeginAsync("management");
        await owner.Context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(token);
        });
        Assert.AreEqual(TransactionOutcome.Committed, (await owner.CommitAsync()).Outcome);
    }

    internal async Task<T> ReadAsync<T>(Func<DbConnection, DbTransaction, CancellationToken, Task<T>> read)
    {
        await using ILocalTransaction owner = await Factory.BeginAsync("management");
        T result = await owner.Context.UseAdoNetAsync(read);
        Assert.AreEqual(TransactionOutcome.NotCommitted, (await owner.RollbackAsync()).Outcome);
        return result;
    }

    internal async Task<long> ScalarAsync(string sql) => await ReadAsync(async (connection, transaction, token) =>
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(token), System.Globalization.CultureInfo.InvariantCulture);
    });

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        foreach (ServiceProvider provider in new[] { first, second })
        {
            try
            {
                await provider.DisposeAsync();
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
        }
        try
        {
            await using NpgsqlConnection connection = await Source.OpenConnectionAsync();
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{Options.Schema}\" CASCADE", connection);
            await cleanup.ExecuteNonQueryAsync();
        }
        catch (Exception error)
        {
            failures.Add(error);
        }
        try
        {
            await Source.DisposeAsync();
        }
        catch (Exception error)
        {
            failures.Add(error);
        }
        finally
        {
            serviceLease.Dispose();
        }
        if (failures.Count > 0)
        {
            throw new AggregateException("Management fixture owned cleanup failed.", failures);
        }
    }

    private static ServiceProvider Provider(DbDataSource source)
    {
        var services = new ServiceCollection();
        services.AddCisAdoNetDatabase("management", _ => source, new()
        {
            SupportedIsolationLevels = [TransactionIsolation.ReadCommitted]
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}

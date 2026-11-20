using System.Data.Common;
using CIS.Transactions;
using CIS.Transactions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Transactions;

internal sealed class PostgresTransactionFixture : IAsyncDisposable
{
    private bool schemaCreated;

    private PostgresTransactionFixture(NpgsqlDataSource source)
    {
        Source = source;
        Schema = "cis_rm02_" + Guid.NewGuid().ToString("N");
        BusinessTable = Qualified("business_record");
        ResultTable = Qualified("operation_result");
        OutgoingTable = Qualified("outgoing_record");

        var services = new ServiceCollection();
        services.AddCisAdoNetDatabase("AcceptanceDatabase", _ => Source, new()
        {
            SupportedIsolationLevels =
            [
                TransactionIsolation.ReadCommitted,
                TransactionIsolation.RepeatableRead,
                TransactionIsolation.Serializable
            ]
        });
        Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    internal NpgsqlDataSource Source
    {
        get;
    }
    internal ServiceProvider Provider
    {
        get;
    }
    internal string Schema
    {
        get;
    }
    internal string BusinessTable
    {
        get;
    }
    internal string ResultTable
    {
        get;
    }
    internal string OutgoingTable
    {
        get;
    }
    internal ILocalTransactionFactory Factory => Provider.GetRequiredService<ILocalTransactionFactory>();

    internal static async Task<PostgresTransactionFixture> CreateAsync()
    {
        string? file = Environment.GetEnvironmentVariable("CIS_RM02_POSTGRES_CONNECTION_FILE");
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
        {
            Assert.Inconclusive("Set CIS_RM02_POSTGRES_CONNECTION_FILE to the private connection-string file for the explicitly provisioned isolated RM-02 PostgreSQL fixture. No default database is used.");
        }
        if (!Path.IsPathFullyQualified(file!))
        {
            throw new InvalidOperationException("The explicit PostgreSQL fixture file path must be absolute.");
        }
        var information = new FileInfo(file!);
        if (!information.Exists || information.Length is <= 0 or > 16_384)
        {
            throw new InvalidOperationException("The explicit PostgreSQL fixture file must be nonempty and at most 16 KiB.");
        }
        string connectionString = (await File.ReadAllTextAsync(information.FullName)).Trim();
        NpgsqlConnectionStringBuilder options;
        try
        {
            options = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("The explicit PostgreSQL fixture file must contain a valid connection string.");
        }
        if (options.Host is not ("127.0.0.1" or "localhost" or "::1")
            || string.IsNullOrWhiteSpace(options.Database) || string.IsNullOrWhiteSpace(options.Username)
            || string.IsNullOrEmpty(options.Password) || !string.IsNullOrEmpty(options.Passfile)
            || !options.Pooling || options.MinPoolSize != 0 || options.MaxPoolSize is <= 0 or > 4
            || options.Timeout != 15 || options.CommandTimeout != 30 || options.CancellationTimeout != 2_000
            || options.Enlist || options.NoResetOnClose || options.SslMode != SslMode.Disable
            || !string.Equals(options["GSS Encryption Mode"]?.ToString(), "Disable", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The fixture requires an explicit loopback database/user/password, no passfile, SSL and GSS encryption disabled for isolated local use, pool 0..4, Timeout=15, Command Timeout=30, Cancellation Timeout=2000, Enlist=false and No Reset On Close=false.");
        }

        var fixture = new PostgresTransactionFixture(NpgsqlDataSource.Create(connectionString));
        try
        {
            await fixture.InitializeAsync();
            return fixture;
        }
        catch (Exception original)
        {
            try
            {
                await fixture.DisposeAsync();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException("PostgreSQL fixture initialization and owned cleanup both failed.", original, cleanup);
            }
            throw;
        }
    }

    internal PostgresAcceptanceConsumer Consumer(Action<string, DbConnection, DbTransaction>? observe = null) =>
        new(BusinessTable, ResultTable, OutgoingTable, observe);

    internal async Task<PostgresTableCounts> CountsAsync(NpgsqlConnection observer)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT (SELECT count(*) FROM {BusinessTable}), (SELECT count(*) FROM {ResultTable}), (SELECT count(*) FROM {OutgoingTable})", observer);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.IsTrue(await reader.ReadAsync());
        return new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    internal async Task<bool> StoredResultIsSuccessfulAsync(NpgsqlConnection observer, Guid operation)
    {
        await using var command = new NpgsqlCommand($"SELECT success FROM {ResultTable} WHERE operation_id = @operation", observer);
        command.Parameters.AddWithValue("operation", operation);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private async Task InitializeAsync()
    {
        await using NpgsqlConnection connection = await Source.OpenConnectionAsync();
        await using (var version = new NpgsqlCommand("SHOW server_version_num", connection))
        {
            int number = int.Parse((string)(await version.ExecuteScalarAsync())!, System.Globalization.CultureInfo.InvariantCulture);
            Assert.IsTrue(number is >= 180_000 and < 190_000, "This reference fixture qualifies the selected PostgreSQL 18 major version.");
        }
        await using (var createSchema = new NpgsqlCommand($"CREATE SCHEMA \"{Schema}\"", connection))
        {
            await createSchema.ExecuteNonQueryAsync();
            schemaCreated = true;
        }
        // Ordinary tables are LOGGED by default. Every test writes at most two tiny rows per table.
        await using var createTables = new NpgsqlCommand($"""
            CREATE TABLE {BusinessTable} (operation_id uuid PRIMARY KEY, payload text NOT NULL CHECK (octet_length(payload) <= 1024));
            CREATE TABLE {ResultTable} (operation_id uuid PRIMARY KEY, success boolean NOT NULL, payload text NOT NULL CHECK (octet_length(payload) <= 1024));
            CREATE TABLE {OutgoingTable} (operation_id uuid PRIMARY KEY, payload text NOT NULL CHECK (octet_length(payload) <= 1024));
            """, connection);
        await createTables.ExecuteNonQueryAsync();

        await using var loggedTables = new NpgsqlCommand("""
            SELECT count(*) FROM pg_class AS relation
            JOIN pg_namespace AS owner ON owner.oid = relation.relnamespace
            WHERE owner.nspname = @schema AND relation.relkind = 'r' AND relation.relpersistence = 'p'
            """, connection);
        loggedTables.Parameters.AddWithValue("schema", Schema);
        Assert.AreEqual(3L, (long)(await loggedTables.ExecuteScalarAsync())!, "The fixture uses exactly three ordinary logged tables.");
    }

    private string Qualified(string table) => $"\"{Schema}\".\"{table}\"";

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        try
        {
            await Provider.DisposeAsync();
        }
        catch (Exception error)
        {
            failures.Add(error);
        }
        try
        {
            if (schemaCreated)
            {
                await using NpgsqlConnection connection = await Source.OpenConnectionAsync();
                await using var command = new NpgsqlCommand($"DROP SCHEMA \"{Schema}\" CASCADE", connection);
                await command.ExecuteNonQueryAsync();
                schemaCreated = false;
            }
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
        if (failures.Count > 0)
        {
            throw new AggregateException("Owned PostgreSQL fixture cleanup failed.", failures);
        }
    }
}

internal readonly record struct PostgresTableCounts(long Business, long Results, long Outgoing);

using System.Data.Common;
using System.Diagnostics;
using CIS.Messaging;
using CIS.Messaging.PostgreSql;
using CIS.Security;
using CIS.Tests.IntegrationTests.Operations;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using CIS.Transactions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Messaging;

// One schema per case on the explicitly provisioned isolated RM-04 fixture, never a default database.
internal sealed class PostgresMessageFixture : IAsyncDisposable
{
    private readonly ServiceProvider provider;
    private bool ownsSchema;

    private PostgresMessageFixture(string connectionString, MessageRetentionPolicy? retention, long? capacity)
    {
        Schema = "cis_rm04_" + Guid.NewGuid().ToString("N");
        var connection = new NpgsqlConnectionStringBuilder(connectionString) { ApplicationName = Schema };
        Source = NpgsqlDataSource.Create(connection.ConnectionString);
        Boundary = new(Source);
        Policy = retention ?? new();
        Store = new(new() { Schema = Schema, Retention = Policy, MaximumRetainedEnvelopeBytes = capacity });
        var services = new ServiceCollection();
        services.AddCisAdoNetDatabase("Messaging", _ => Boundary, new()
        {
            SupportedIsolationLevels = [TransactionIsolation.ReadCommitted, TransactionIsolation.RepeatableRead, TransactionIsolation.Serializable]
        });
        provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    internal string Schema { get; }
    internal NpgsqlDataSource Source { get; }
    internal NativeCommitBoundarySource Boundary { get; }
    internal PostgreSqlMessagingStore Store { get; }
    internal MessageRetentionPolicy Policy { get; }
    internal ILocalTransactionFactory Factory => provider.GetRequiredService<ILocalTransactionFactory>();
    internal const string Application = "rm04-service";
    internal const string EnvironmentName = "test";

    internal static async Task<PostgresMessageFixture> CreateAsync(MessageRetentionPolicy? retention = null, long? capacity = null)
    {
        string? path = System.Environment.GetEnvironmentVariable("CIS_RM04_POSTGRES_CONNECTION_FILE");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Assert.Inconclusive("Set CIS_RM04_POSTGRES_CONNECTION_FILE to the private absolute file for the newly owned isolated RM-04 PostgreSQL fixture.");
        }
        if (!Path.IsPathFullyQualified(path!))
        {
            throw new InvalidOperationException("The explicit RM-04 connection file must be absolute.");
        }
        var file = new FileInfo(path!);
        if (file.Length is <= 0 or > 16384)
        {
            throw new InvalidOperationException("The explicit private connection file must contain at most 16 KiB.");
        }
        var builder = new NpgsqlConnectionStringBuilder((await File.ReadAllTextAsync(path!)).Trim());
        if (builder.Host is not ("localhost" or "127.0.0.1" or "::1") || string.IsNullOrWhiteSpace(builder.Database)
            || string.IsNullOrWhiteSpace(builder.Username) || string.IsNullOrEmpty(builder.Password) || !string.IsNullOrEmpty(builder.Passfile)
            || !builder.Pooling || builder.MinPoolSize != 0 || builder.MaxPoolSize is < 1 or > 6 || builder.Enlist || builder.NoResetOnClose
            || builder.Timeout != 15 || builder.CommandTimeout != 30 || builder.CancellationTimeout != 2000
            || builder.SslMode != SslMode.Disable || !string.Equals(builder["GSS Encryption Mode"]?.ToString(), "Disable", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Use only explicit isolated loopback credentials, pool0..6, timeouts15/30/2000 and disabled SSL/GSS/ambient enlistment.");
        }
        var fixture = new PostgresMessageFixture(builder.ConnectionString, retention, capacity);
        try
        {
            fixture.ownsSchema = true;
            await using ILocalTransaction install = await fixture.Factory.BeginAsync("Messaging");
            await PostgreSqlMessagingSchema.InstallAsync(install.Context, fixture.Schema);
            Assert.AreEqual(TransactionOutcome.Committed, (await install.CommitAsync()).Outcome);
            await fixture.SqlAsync($"CREATE TABLE {fixture.Table("business_record")} (message_id uuid NOT NULL, subscription text NOT NULL, value integer NOT NULL, native_xid xid8 NOT NULL DEFAULT pg_current_xact_id(), PRIMARY KEY(message_id,subscription))");
            await using NpgsqlConnection connection = await fixture.Source.OpenConnectionAsync();
            await using (var budget = new NpgsqlCommand($"INSERT INTO {fixture.Table("storage_budget")}(application,environment,maximum_bytes) VALUES (@application,@environment,@maximum)", connection))
            {
                budget.Parameters.AddWithValue("application", Application);
                budget.Parameters.AddWithValue("environment", EnvironmentName);
                budget.Parameters.Add(new NpgsqlParameter("maximum", NpgsqlTypes.NpgsqlDbType.Bigint) { Value = (object?)capacity ?? DBNull.Value });
                await budget.ExecuteNonQueryAsync();
            }
            await using var check = new NpgsqlCommand("SELECT current_setting('server_version_num')::integer, current_setting('fsync'), current_setting('synchronous_commit'), current_setting('full_page_writes')", connection);
            await using NpgsqlDataReader reader = await check.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.IsTrue(reader.GetInt32(0) is >= 180000 and < 190000);
            Assert.AreEqual("on", reader.GetString(1));
            Assert.AreEqual("on", reader.GetString(2));
            Assert.AreEqual("on", reader.GetString(3));
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    internal string Table(string name) => $"\"{Schema}\".\"{name}\"";

    internal static EncodedMessage Message(Guid? id = null, int value = 1) => MessageWire.Encode(new(id ?? Guid.NewGuid(), MessageKind.Event,
        new("country.updated", 1), "producer", EnvironmentName, DateTimeOffset.UnixEpoch,
        System.Text.Encoding.UTF8.GetBytes("{\"value\":" + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}")));

    internal static MessageProvenance Provenance(string user = "producer") => new("Broker", "rm04", new("rabbit:Broker/rm04", user), user, "producer", EnvironmentName);

    internal static MessageProcessingAttempt Attempt(EncodedMessage message, string subscription = "Countries")
        => new(new(Application, EnvironmentName, subscription, message.Envelope.MessageId), message, Provenance());

    internal static MessageOutgoingRecord Outgoing(EncodedMessage message, string route = "CountryEvents")
        => new(Guid.NewGuid(), Application, EnvironmentName, new(route), message);

    internal static MessageOutgoingRecord Handoff(MessageProcessingAttempt attempt, bool terminal = false)
        => new(Guid.NewGuid(), Application, EnvironmentName, new("CountryEvents"), attempt.Message,
            terminal ? MessageRecordPurpose.Terminal : MessageRecordPurpose.Retry, ProcessingKey: attempt.Key,
            DeliveryOrdinal: terminal ? attempt.DeliveryOrdinal : attempt.DeliveryOrdinal + 1, Generation: attempt.Generation,
            DiagnosticCode: "messaging.test_failure", OriginalProvenance: attempt.Provenance, TargetSubscription: attempt.Key.Subscription);

    internal Task InsertBusinessAsync(ILocalTransactionContext context, Guid id, string subscription = "Countries")
        => context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            await using DbCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"INSERT INTO {Table("business_record")} (message_id,subscription,value) VALUES (@id,@subscription,1)";
            Add(command, "id", id);
            Add(command, "subscription", subscription);
            await command.ExecuteNonQueryAsync(token);
        });

    internal async Task<int> BackendAsync(ILocalTransactionContext context)
        => await context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            await using DbCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT pg_backend_pid()";
            return (int)(await command.ExecuteScalarAsync(token))!;
        });

    internal async Task WaitForLockAsync(int backend)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            await using NpgsqlConnection connection = await Source.OpenConnectionAsync();
            await using var command = new NpgsqlCommand("SELECT wait_event_type='Lock' FROM pg_stat_activity WHERE pid=@pid", connection);
            command.Parameters.AddWithValue("pid", backend);
            if (await command.ExecuteScalarAsync() is true)
            {
                return;
            }
            await Task.Delay(10);
        }
        Assert.Fail("The competing native backend did not reach database uniqueness/row arbitration.");
    }

    internal async Task<long> CountAsync(string table)
    {
        await using NpgsqlConnection connection = await Source.OpenConnectionAsync();
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {Table(table)}", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    internal async Task SqlAsync(string sql)
    {
        await using NpgsqlConnection connection = await Source.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    internal async Task<MessageReservation[]> ReserveCommittedAsync()
    {
        await using ILocalTransaction owner = await Factory.BeginAsync("Messaging");
        MessageReservation[] rows = (await Store.ReserveAsync(owner.Context, new(Application, EnvironmentName, Guid.NewGuid()))).ToArray();
        Assert.AreEqual(TransactionOutcome.Committed, (await owner.CommitAsync()).Outcome);
        return rows;
    }

    internal async Task PublishConfirmedAsync(MessageReservation reservation)
    {
        await using ILocalTransaction owner = await Factory.BeginAsync("Messaging");
        Assert.IsTrue(await Store.RecordPublicationAsync(owner.Context, new(reservation, new(MessagePublicationDisposition.Confirmed))));
        Assert.AreEqual(TransactionOutcome.Committed, (await owner.CommitAsync()).Outcome);
    }

    private static void Add(DbCommand command, string name, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        try
        {
            await provider.DisposeAsync();
        }
        catch (Exception error)
        {
            failures.Add(error);
        }
        try
        {
            if (ownsSchema)
            {
                await SqlAsync($"DROP SCHEMA IF EXISTS \"{Schema}\" CASCADE");
                ownsSchema = false;
            }
        }
        catch (Exception error)
        {
            failures.Add(error);
        }
        try
        {
            await Boundary.DisposeAsync();
            await Source.DisposeAsync();
        }
        catch (Exception error)
        {
            failures.Add(error);
        }
        if (failures.Count != 0)
        {
            throw new AggregateException("Owned messaging PostgreSQL fixture cleanup failed.", failures);
        }
    }
}

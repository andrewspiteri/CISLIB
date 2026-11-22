using System.Data.Common;
using CIS.Operations;
using CIS.Operations.Hosting;
using CIS.Operations.PostgreSql;
using CIS.Security;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using CIS.Transactions.Hosting;
using CIS.Tests.IntegrationTests.Infrastructure.Containers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Operations;

// Each test owns its schema and clients on the run's automatically owned PostgreSQL service.
internal sealed class PostgresOperationFixture : IAsyncDisposable
{
    private bool schemaCreated;
    private readonly ServiceProvider provider;
    private readonly OwnedPostgresService database;
    private IDisposable? serviceLease;

    private PostgresOperationFixture(NpgsqlDataSource source, OwnedPostgresService database, IDisposable serviceLease,
        int commandTimeout, OperationRetentionPolicy? retention, bool nativeCommitBoundary, bool maintenanceEnabled,
        string? existingSchema)
    {
        this.database = database;
        this.serviceLease = serviceLease;
        Source = source;
        CommitBoundary = nativeCommitBoundary ? new(source) : null;
        Schema = existingSchema ?? "cis_rm03_" + Guid.NewGuid().ToString("N");
        Journal = new(new()
        {
            Schema = Schema,
            CommandTimeoutSeconds = commandTimeout
        });
        var services = new ServiceCollection();
        services.AddCisAdoNetDatabase("Ledger", _ => (DbDataSource?)CommitBoundary ?? Source, new()
        {
            SupportedIsolationLevels =
            [TransactionIsolation.ReadCommitted, TransactionIsolation.RepeatableRead, TransactionIsolation.Serializable]
        });
        services.AddSingleton<IOperationJournal>(Journal);
        services.AddSingleton<ICallerContextAccessor>(new OperationFixtureCaller());
        services.AddSingleton<ICapabilityAuthorizer>(Authorizer);
        services.AddCisOperations();
        services.AddCisOperation(new OperationDefinition<DebitInput, DebitReceipt>("Debit", "Ledger", new("debit-input", 1),
            new DebitNormalizer(), Codec, _ => new DebitScopeResolver(), new()
            {
                ExecuteCapabilities = ["debit.submit", "debit.read"],
                LookupCapabilities = ["debit.read"],
                MaintenanceCapabilities = maintenanceEnabled ? ["debit.maintain"] : [],
                Retention = retention ?? new()
            }));
        services.AddCisPostgreSqlOperationRetentionMaintenance();
        provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    internal NpgsqlDataSource Source
    {
        get;
    }

    internal NativeCommitBoundarySource? CommitBoundary
    {
        get;
    }

    internal string Schema
    {
        get;
    }

    internal PostgreSqlOperationJournal Journal
    {
        get;
    }

    internal DebitCodec Codec { get; } = new();

    internal OperationFixtureAuthorizer Authorizer { get; } = new();

    internal ILocalTransactionFactory Factory => provider.GetRequiredService<ILocalTransactionFactory>();

    internal LocalApplicationIdentity Application => provider.GetRequiredService<LocalApplicationIdentity>();

    internal string JournalTable => Qualified("operation_journal");

    internal string BusinessTable => Qualified("business_record");

    internal string OutgoingTable => Qualified("outgoing_record");

    internal AsyncServiceScope CreateScope() => provider.CreateAsyncScope();

    internal static OperationRequest<DebitInput> Request(int amount = 10, string key = "key", bool accepted = true,
        string scope = "alpha") => new("Debit", scope, key, new("account", amount, accepted));

    internal static async Task<PostgresOperationFixture> CreateAsync(int commandTimeout = 15,
        OperationRetentionPolicy? retention = null, bool nativeCommitBoundary = false, bool maintenanceEnabled = true,
        OwnedPostgresService? database = null)
    {
        database ??= await IntegrationServices.GetPostgresAsync();
        return await CreateOwnedAsync(database, commandTimeout, retention, nativeCommitBoundary, maintenanceEnabled, null);
    }

    internal static async Task<PostgresOperationFixture> ReopenAsync(PostgresOperationFixture previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        PostgresOperationFixture replacement = await CreateOwnedAsync(previous.database, 15, null, false, true, previous.Schema);
        replacement.schemaCreated = true;
        previous.schemaCreated = false;
        return replacement;
    }

    private static async Task<PostgresOperationFixture> CreateOwnedAsync(OwnedPostgresService database, int commandTimeout,
        OperationRetentionPolicy? retention, bool nativeCommitBoundary, bool maintenanceEnabled, string? existingSchema)
    {
        IDisposable lease = database.AcquireClientLease();
        NpgsqlDataSource? source = null;
        PostgresOperationFixture? fixture = null;
        try
        {
            source = NpgsqlDataSource.Create(database.CreateConnectionString(6));
            fixture = new(source, database, lease, commandTimeout, retention, nativeCommitBoundary, maintenanceEnabled, existingSchema);
            await fixture.InitializeAsync(installSchema: existingSchema is null);
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
                throw new AggregateException("PostgreSQL operation initialization and owned cleanup both failed.", original, cleanup);
            }
            throw;
        }
    }

    internal async Task<OperationDecision<DebitReceipt>> HandleAsync(ILocalTransactionContext context, DebitInput input,
        CancellationToken token)
    {
        if (!input.Accepted)
        {
            return OperationDecision<DebitReceipt>.Reject("debit.declined", "The application declined this debit.");
        }
        var receipt = new DebitReceipt(Guid.NewGuid(), input.Amount);
        await context.UseAdoNetAsync(async (connection, transaction, workToken) =>
        {
            Assert.AreSame(connection, transaction.Connection);
            await using DbCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"INSERT INTO {BusinessTable} (record_id, payload) VALUES (@id, @payload)";
            Add(command, "id", receipt.Identifier);
            Add(command, "payload", "debit:" + input.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(workToken);
        }, token);
        await context.UseAdoNetAsync(async (connection, transaction, workToken) =>
        {
            Assert.AreSame(connection, transaction.Connection);
            await using DbCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"INSERT INTO {OutgoingTable} (record_id, payload) VALUES (@id, @payload)";
            Add(command, "id", receipt.Identifier);
            Add(command, "payload", "debit-recorded");
            await command.ExecuteNonQueryAsync(workToken);
        }, token);
        return OperationDecision<DebitReceipt>.Succeed(receipt);
    }

    internal async Task<OperationTableCounts> CountsAsync(NpgsqlConnection observer)
    {
        await using var command = new NpgsqlCommand($"""
            SELECT (SELECT count(*) FROM {JournalTable}), (SELECT count(*) FROM {BusinessTable}),
                (SELECT count(*) FROM {OutgoingTable})
            """, observer);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.IsTrue(await reader.ReadAsync());
        return new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    internal async Task ExecuteAsync(string sql)
    {
        await using NpgsqlConnection connection = await Source.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    internal static Task<int> PidAsync(ILocalTransactionContext context) => context.UseAdoNetAsync(async (connection, transaction, token) =>
    {
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT pg_backend_pid()";
        return (int)(await command.ExecuteScalarAsync(token))!;
    });

    internal static async Task ConfirmBlockedAsync(NpgsqlConnection observer, int waiter, int blocker)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        for (int attempt = 0; attempt < 512; attempt++)
        {
            await using var command = new NpgsqlCommand("SELECT @blocker = ANY(pg_blocking_pids(@waiter))", observer);
            command.Parameters.AddWithValue("blocker", blocker);
            command.Parameters.AddWithValue("waiter", waiter);
            if (await command.ExecuteScalarAsync(cancellation.Token) is true)
            {
                return;
            }
            // This only spaces observer traffic; the server's blocking graph is the evidence.
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellation.Token);
        }
        Assert.Fail("The independent observer did not confirm the owned blocking relationship.");
    }

    internal string Qualified(string table) => $"\"{Schema}\".\"{table}\"";

    internal static void Add(DbCommand command, string name, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private async Task InitializeAsync(bool installSchema)
    {
        await using NpgsqlConnection connection = await Source.OpenConnectionAsync();
        await using (var version = new NpgsqlCommand("SELECT current_setting('server_version_num')::integer, current_setting('fsync'), current_setting('synchronous_commit'), current_setting('full_page_writes')", connection))
        {
            await using NpgsqlDataReader reader = await version.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.IsTrue(reader.GetInt32(0) is >= 180_000 and < 190_000);
            Assert.AreEqual("on", reader.GetString(1));
            Assert.AreEqual("on", reader.GetString(2));
            Assert.AreEqual("on", reader.GetString(3));
        }
        if (installSchema)
        {
            // A new installation owns partial state; reconstruction borrows the previous owner's schema until it succeeds.
            schemaCreated = true;
            await using (var install = new NpgsqlCommand(PostgreSqlOperationSchema.GetInstallationSql(Schema), connection))
            {
                await install.ExecuteNonQueryAsync();
            }
            await using var tables = new NpgsqlCommand($"""
            CREATE TABLE {BusinessTable}
                (record_id uuid PRIMARY KEY, payload text NOT NULL CHECK (octet_length(payload) <= 4096), native_xid xid8 NOT NULL DEFAULT pg_current_xact_id());
            CREATE TABLE {OutgoingTable}
                (record_id uuid PRIMARY KEY, payload text NOT NULL CHECK (octet_length(payload) <= 4096), native_xid xid8 NOT NULL DEFAULT pg_current_xact_id());
            """, connection);
            await tables.ExecuteNonQueryAsync();
        }
        await using var logged = new NpgsqlCommand("""
            SELECT count(*) FROM pg_class AS relation JOIN pg_namespace AS owner ON owner.oid = relation.relnamespace
            WHERE owner.nspname = @schema AND relation.relkind = 'r' AND relation.relpersistence = 'p'
            """, connection);
        logged.Parameters.AddWithValue("schema", Schema);
        Assert.AreEqual(4L, await logged.ExecuteScalarAsync(), "The fixture uses exactly four ordinary logged tables.");
    }

    public async ValueTask DisposeAsync()
    {
        var errors = new List<Exception>();
        try
        {
            await provider.DisposeAsync();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        try
        {
            if (schemaCreated)
            {
                await using NpgsqlConnection connection = await Source.OpenConnectionAsync();
                await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{Schema}\" CASCADE", connection);
                await command.ExecuteNonQueryAsync();
                schemaCreated = false;
            }
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        try
        {
            await Source.DisposeAsync();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        if (errors.Count > 0)
        {
            throw new AggregateException("Owned operation PostgreSQL fixture cleanup failed.", errors);
        }
        Interlocked.Exchange(ref serviceLease, null)?.Dispose();
    }
}

internal readonly record struct OperationTableCounts(long Journal, long Business, long Outgoing);

internal sealed record DebitInput(string Account, int Amount, bool Accepted);

internal sealed record DebitReceipt(Guid Identifier, int Amount);

internal sealed class DebitNormalizer : IOperationRequestNormalizer<DebitInput>
{
    public NormalizedOperationInput<DebitInput> Normalize(DebitInput input, OperationScope scope) =>
        new(new(scope.ResourceIdentifier, input.Amount, input.Accepted), [new(new("debit-input", 1),
            OperationCanonicalValue.Sequence([OperationCanonicalValue.Text(scope.Scope), OperationCanonicalValue.Text(scope.ResourceIdentifier),
                OperationCanonicalValue.Integer(input.Amount), OperationCanonicalValue.Boolean(input.Accepted)]))]);
}

internal sealed class DebitScopeResolver : IOperationScopeResolver
{
    public ValueTask<OperationScope> ResolveAsync(string operationName, string requestedScope, CallerContext? caller,
        CancellationToken cancellationToken = default) => ValueTask.FromResult(requestedScope switch
        {
            "alpha" => new OperationScope("tenant-alpha", "account", "account-alpha"),
            "beta" => new OperationScope("tenant-beta", "account", "account-beta"),
            _ => throw new ArgumentException("This fixture maps only two known application scopes.")
        });
}

internal sealed class OperationFixtureCaller : ICallerContextAccessor
{
    public CallerContext? Current => null;
}

internal sealed class OperationFixtureAuthorizer : ICapabilityAuthorizer
{
    internal int Calls;

    internal CapabilityTarget? Target;

    internal IReadOnlyList<string>? Capabilities;

    internal bool Allowed { get; set; } = true;

    public ValueTask<AuthorizationDecision> AuthorizeAsync(IReadOnlyList<string> capabilities, CapabilityTarget target,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        Target = target;
        Capabilities = capabilities;
        return ValueTask.FromResult(Allowed ? AuthorizationDecision.Allow
            : AuthorizationDecision.Deny(new(SecurityFailureKind.CapabilityDenied, "The fixture denied this operation.")));
    }
}

internal sealed class DebitCodec : IOperationResultCodec<DebitReceipt>
{
    public OperationContract CurrentContract { get; } = new("debit-receipt", 1);

    public IReadOnlyList<OperationContract> ReadableContracts { get; } = [new("debit-receipt", 1)];

    internal bool RejectDecode
    {
        get; set;
    }

    public byte[] Encode(DebitReceipt? value)
    {
        ArgumentNullException.ThrowIfNull(value);
        byte[] bytes = new byte[20];
        value.Identifier.TryWriteBytes(bytes);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), value.Amount);
        return bytes;
    }

    public bool TryDecode(OperationContract contract, ReadOnlyMemory<byte> value, out DebitReceipt? decoded)
    {
        if (RejectDecode || contract != CurrentContract || value.Length != 20)
        {
            decoded = null;
            return false;
        }
        decoded = new(new Guid(value.Span[..16]), System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(value.Span[16..]));
        return true;
    }
}

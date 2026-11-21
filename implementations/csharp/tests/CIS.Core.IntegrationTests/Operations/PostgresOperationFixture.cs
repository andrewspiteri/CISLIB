using System.Data.Common;
using CIS.Operations;
using CIS.Operations.Hosting;
using CIS.Operations.PostgreSql;
using CIS.Security;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using CIS.Transactions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Operations;

// Each test owns a fresh schema on the explicitly selected isolated fixture; no default database is used.
internal sealed class PostgresOperationFixture : IAsyncDisposable
{
    private bool schemaCreated;
    private readonly ServiceProvider provider;

    private PostgresOperationFixture(NpgsqlDataSource source, int commandTimeout, OperationRetentionPolicy? retention,
        bool nativeCommitBoundary, bool maintenanceEnabled)
    {
        Source = source;
        CommitBoundary = nativeCommitBoundary ? new(source) : null;
        Schema = "cis_rm03_" + Guid.NewGuid().ToString("N");
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
        OperationRetentionPolicy? retention = null, bool nativeCommitBoundary = false, bool maintenanceEnabled = true)
    {
        string? file = Environment.GetEnvironmentVariable("CIS_RM03_POSTGRES_CONNECTION_FILE");
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
        {
            Assert.Inconclusive("Set CIS_RM03_POSTGRES_CONNECTION_FILE to the private absolute connection-file path for the explicitly provisioned isolated RM-03 fixture.");
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
            options = new(connectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("The explicit PostgreSQL fixture file must contain a valid connection string.");
        }
        if (options.Host is not ("127.0.0.1" or "localhost" or "::1")
            || string.IsNullOrWhiteSpace(options.Database) || string.IsNullOrWhiteSpace(options.Username)
            || string.IsNullOrEmpty(options.Password) || !string.IsNullOrEmpty(options.Passfile)
            || !options.Pooling || options.MinPoolSize != 0 || options.MaxPoolSize is <= 0 or > 6
            || options.Timeout != 15 || options.CommandTimeout != 30 || options.CancellationTimeout != 2_000
            || options.Enlist || options.NoResetOnClose || options.SslMode != SslMode.Disable
            || !string.Equals(options["GSS Encryption Mode"]?.ToString(), "Disable", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The owned loopback fixture requires explicit database/user/password, no passfile, pool0..6, timeouts15/30/2000, Enlist=false, No Reset On Close=false and isolated SSL/GSS Disable.");
        }

        var fixture = new PostgresOperationFixture(NpgsqlDataSource.Create(connectionString), commandTimeout,
            retention, nativeCommitBoundary, maintenanceEnabled);
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

    private async Task InitializeAsync()
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
        // Record ownership before sending installation: partial successful DDL must also be removed.
        schemaCreated = true;
        await using (var install = new NpgsqlCommand(PostgreSqlOperationSchema.GetInstallationSql(Schema), connection))
        {
            await install.ExecuteNonQueryAsync();
        }
        await using (var tables = new NpgsqlCommand($"""
            CREATE TABLE {BusinessTable}
                (record_id uuid PRIMARY KEY, payload text NOT NULL CHECK (octet_length(payload) <= 4096), native_xid xid8 NOT NULL DEFAULT pg_current_xact_id());
            CREATE TABLE {OutgoingTable}
                (record_id uuid PRIMARY KEY, payload text NOT NULL CHECK (octet_length(payload) <= 4096), native_xid xid8 NOT NULL DEFAULT pg_current_xact_id());
            """, connection))
        {
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

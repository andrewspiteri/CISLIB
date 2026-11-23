using System.Data.Common;
using CIS.Coordination;
using CIS.Coordination.PostgreSql;
using CIS.Tests.IntegrationTests.Infrastructure.Containers;
using CIS.Tests.IntegrationTests.Operations;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using CIS.Transactions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Coordination.PostgreSql;

// Per-case schema and clients on the run-owned typed service. No ambient service descriptor or Docker command.
internal sealed class PostgresCoordinationFixture : IAsyncDisposable
{
    private readonly ServiceProvider services;
    private IDisposable? serviceLease;
    private bool schemaCreated;

    private PostgresCoordinationFixture(NpgsqlDataSource source, IDisposable serviceLease, bool boundary,
        string? existingSchema = null, Guid? existingAuthority = null)
    {
        Source = source;
        this.serviceLease = serviceLease;
        Boundary = boundary ? new(source) : null;
        Schema = existingSchema ?? "cis_rm05_" + Guid.NewGuid().ToString("N");
        AuthorityId = existingAuthority ?? Guid.NewGuid();
        var registrations = new ServiceCollection();
        DbDataSource selected = (DbDataSource?)Boundary ?? Source;
        registrations.AddCisAdoNetDatabase("Coordination", _ => selected, new()
        {
            SupportedIsolationLevels = [TransactionIsolation.ReadCommitted, TransactionIsolation.RepeatableRead]
        });
        registrations.AddCisAdoNetDatabase("Other", _ => Source, new()
        {
            SupportedIsolationLevels = [TransactionIsolation.ReadCommitted]
        });
        services = registrations.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Authority = NewAuthority();
    }

    internal NpgsqlDataSource Source
    {
        get;
    }
    internal NativeCommitBoundarySource? Boundary
    {
        get;
    }
    internal string Schema
    {
        get;
    }
    internal Guid AuthorityId
    {
        get;
    }
    internal ILocalTransactionFactory Factory => services.GetRequiredService<ILocalTransactionFactory>();
    internal PostgreSqlCoordinationProvider Authority
    {
        get;
    }
    internal string Grants => Qualified("ownership_grant");
    internal string Business => Qualified("business_record");

    internal PostgreSqlCoordinationProvider NewAuthority(Guid? authorityId = null) => new(Factory, "Coordination", new()
    {
        AuthorityId = authorityId ?? AuthorityId,
        Schema = Schema,
        CommandTimeoutSeconds = 15
    });

    internal static async Task<PostgresCoordinationFixture> CreateAsync(OwnedPostgresService? database = null, bool boundary = false)
    {
        database ??= await IntegrationServices.GetPostgresAsync();
        IDisposable lease = database.AcquireClientLease();
        NpgsqlDataSource? source = null;
        PostgresCoordinationFixture? fixture = null;
        try
        {
            source = NpgsqlDataSource.Create(database.CreateConnectionString(6));
            fixture = new(source, lease, boundary);
            fixture.schemaCreated = true;
            await fixture.ExecuteAsync(PostgreSqlCoordinationSchema.GetInstallationSql(fixture.AuthorityId, fixture.Schema));
            await fixture.ExecuteAsync($"CREATE TABLE {fixture.Business} (effect_id uuid PRIMARY KEY, generation bigint NOT NULL)");
            await fixture.Authority.ValidateAsync();
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
                throw new AggregateException("Coordination fixture initialization and cleanup both failed.", original, cleanup);
            }
            throw;
        }
    }

    internal static async Task<PostgresCoordinationFixture> ReopenAsync(PostgresCoordinationFixture previous, OwnedPostgresService database)
    {
        IDisposable lease = database.AcquireClientLease();
        NpgsqlDataSource? source = null;
        PostgresCoordinationFixture? replacement = null;
        try
        {
            source = NpgsqlDataSource.Create(database.CreateConnectionString(6));
            replacement = new(source, lease, boundary: false, previous.Schema, previous.AuthorityId);
            await replacement.Authority.ValidateAsync();
            // The old owner retains schema custody if reconstruction fails.
            replacement.schemaCreated = true;
            previous.schemaCreated = false;
            return replacement;
        }
        catch (Exception original)
        {
            try
            {
                if (replacement is not null)
                {
                    await replacement.DisposeAsync();
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
                throw new AggregateException("Coordination reconstruction and client cleanup both failed.", original, cleanup);
            }
            throw;
        }
    }

    internal CoordinationAcquireRequest Request(string resource = "partition", string instance = "instance-a", TimeSpan? duration = null)
        => new(new(new("test", "wallet-service", "wallet.compaction", resource), "wallet-service", instance, Guid.NewGuid()),
            duration ?? TimeSpan.FromSeconds(30));

    internal async Task<CoordinationGrant> AcquireAsync(string resource = "partition", string instance = "instance-a")
    {
        CoordinationAcquireResult result = await Authority.AcquireAsync(Request(resource, instance));
        Assert.AreEqual(CoordinationAcquireOutcome.Acquired, result.Outcome);
        Assert.IsNotNull(result.Grant);
        return result.Grant;
    }

    internal async Task ExecuteAsync(string sql)
    {
        await using NpgsqlConnection connection = await Source.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    internal async Task<long> ScalarAsync(string sql)
    {
        await using NpgsqlConnection connection = await Source.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    internal string Qualified(string relation) => $"\"{Schema}\".\"{relation}\"";

    internal static Task<int> PidAsync(ILocalTransactionContext context) => context.UseAdoNetAsync(async (connection, transaction, token) =>
    {
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT pg_backend_pid()";
        return (int)(await command.ExecuteScalarAsync(token))!;
    });

    internal static async Task ConfirmBlockedAsync(NpgsqlConnection observer, int waiter, int blocker)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        for (int attempt = 0; attempt < 512; attempt++)
        {
            await using var command = new NpgsqlCommand("SELECT @blocker = ANY(pg_blocking_pids(@waiter))", observer);
            command.Parameters.AddWithValue("blocker", blocker);
            command.Parameters.AddWithValue("waiter", waiter);
            if (await command.ExecuteScalarAsync(cancellation.Token) is true)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellation.Token);
        }
        Assert.Fail("The server did not confirm the owned coordination blocking relationship.");
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        try
        {
            await services.DisposeAsync();
        }
        catch (Exception error)
        {
            failures.Add(error);
        }
        try
        {
            if (schemaCreated)
            {
                await ExecuteAsync($"DROP SCHEMA IF EXISTS \"{Schema}\" CASCADE");
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
        if (failures.Count == 0)
        {
            Interlocked.Exchange(ref serviceLease, null)?.Dispose();
        }
        else
        {
            throw new AggregateException("Coordination fixture cleanup did not settle completely.", failures);
        }
    }
}

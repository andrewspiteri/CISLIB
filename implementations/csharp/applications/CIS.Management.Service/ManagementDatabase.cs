using CIS.Heartbeat;
using CIS.Management.Service.Persistence;
using CIS.Messaging.PostgreSql;
using CIS.Operations.PostgreSql;
using CIS.Startup;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using CIS.Management.Service.Backends;

namespace CIS.Management.Service;

internal sealed class ManagementDatabase(IServiceScopeFactory scopes, ManagementServiceOptions options,
    PostgreSqlManagementAuthority authority, IHeartbeatOwnerFactory heartbeat, TimeProvider? timeProvider = null)
    : IStartable, IHeartbeatSubscriber, IAsyncDisposable
{
    private IHeartbeatOwner? owner;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private long lastCleanup;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
        {
            await ValidateAsync(scope.ServiceProvider.GetRequiredService<ILocalTransactionFactory>(), options, cancellationToken);
            IManagementBackend backend = scope.ServiceProvider.GetRequiredService<IManagementBackend>();
            backend.Capabilities.Validate(options.Backend.Mode);
            _ = await backend.ProbeAsync(cancellationToken);
        }
        lastCleanup = clock.GetTimestamp();
        IHeartbeatOwner candidate = heartbeat.CreateOwner();
        try
        {
            candidate.Service.Register(this, new()
            {
                Name = "management-authority-and-retention",
                Interval = TimeSpan.FromSeconds(5),
                InitialDelay = TimeSpan.FromSeconds(5)
            });
            owner = candidate;
        }
        catch (Exception original)
        {
            try
            {
                await candidate.DisposeAsync();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(original, cleanup);
            }
            throw;
        }
    }

    public async Task<bool> OnElapsedAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        IManagementBackend backend = scope.ServiceProvider.GetRequiredService<IManagementBackend>();
        _ = await backend.ProbeAsync(cancellationToken);
        if (options.Configuration?.Manifest.RuleEngine is { Count: > 0 })
        {
            _ = await scope.ServiceProvider.GetRequiredService<PostgreSqlManagementStore>()
                .ProjectRuleEngineCountersAsync(Math.Min(16, options.Persistence.CleanupBatchSize), cancellationToken);
        }
        if (clock.GetElapsedTime(lastCleanup) >= TimeSpan.FromMinutes(1))
        {
            lastCleanup = clock.GetTimestamp();
            return (await backend.CleanupAsync(options.Persistence.CleanupBatchSize, cancellationToken)).IsKnownCommitted;
        }
        return true;
    }

    // Heartbeat owns the bounded error observation; no repeated module-level error stream is added.
    public Task OnErrorAsync(Exception exception)
    {
        authority.MarkUnavailable();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        authority.StopObserving();
        if (owner is not null)
        {
            await owner.DisposeAsync();
        }
    }

    internal static async Task InstallAsync(ILocalTransactionFactory transactions, ManagementServiceOptions options,
        CancellationToken cancellationToken)
    {
        await using ILocalTransaction owner = await transactions.BeginAsync("management", cancellationToken);
        await ManagementSchema.InstallAsync(owner.Context, options.Persistence.Schema, options.Persistence, cancellationToken, options.Backend);
        await InstallDependencyAsync(owner.Context, options.OperationsSchema, PostgreSqlOperationSchema.Version,
            PostgreSqlOperationSchema.GetInstallationSql(options.OperationsSchema), cancellationToken);
        await InstallDependencyAsync(owner.Context, options.MessagingSchema, PostgreSqlMessagingSchema.Version,
            PostgreSqlMessagingSchema.GetInstallationSql(options.MessagingSchema), cancellationToken);
        _ = await new PostgreSqlManagementAuthority(transactions, options).ReadAsync(owner.Context, false, cancellationToken);
        TransactionCompletion completion = await owner.CommitAsync(cancellationToken);
        if (completion.Outcome != TransactionOutcome.Committed || completion.CleanupErrors.Count != 0)
        {
            throw new ManagementSchemaException("Explicit management migration did not establish clean committed completion.");
        }
    }

    private static Task InstallDependencyAsync(ILocalTransactionContext context, string schema, int expectedVersion,
        string installationSql, CancellationToken cancellationToken)
        => context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            string relation = ManagementSql.Table(schema, "schema_version");
            await using var exists = ManagementSql.Command(connection, transaction, "SELECT to_regclass(@relation) IS NOT NULL", ("relation", relation));
            if (await exists.ExecuteScalarAsync(token) is true)
            {
                await using var version = ManagementSql.Command(connection, transaction, $"SELECT version FROM {relation} WHERE singleton");
                if (await version.ExecuteScalarAsync(token) is not int actual || actual != expectedVersion)
                {
                    throw new ManagementSchemaException("The existing operation/delivery schema requires its own explicit supported migration.");
                }
            }
            else
            {
                await using var install = ManagementSql.Command(connection, transaction, installationSql);
                install.CommandTimeout = 60;
                await install.ExecuteNonQueryAsync(token);
            }
        }, cancellationToken);

    internal static async Task ValidateAsync(ILocalTransactionFactory transactions, ManagementServiceOptions options,
        CancellationToken cancellationToken)
    {
        await using ILocalTransaction owner = await transactions.BeginAsync("management", cancellationToken);
        await ManagementSchema.ValidateAsync(owner.Context, options.Persistence.Schema, cancellationToken);
        await owner.Context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            foreach ((string schema, int expected) in new[] { (options.OperationsSchema, PostgreSqlOperationSchema.Version),
                (options.MessagingSchema, PostgreSqlMessagingSchema.Version) })
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandTimeout = 15;
                command.CommandText = $"SELECT version FROM \"{schema}\".schema_version WHERE singleton";
                if (await command.ExecuteScalarAsync(token) is not int version || version != expected)
                {
                    throw new ManagementSchemaException("Install the supported management dependency schemas explicitly before startup.");
                }
            }
        }, cancellationToken);
        TransactionCompletion completion = await owner.RollbackAsync();
        if (completion.CleanupErrors.Count != 0 || !completion.RollbackConfirmed)
        {
            throw new ManagementSchemaException("Management schema readiness did not finish its actual read cleanup.");
        }
    }
}

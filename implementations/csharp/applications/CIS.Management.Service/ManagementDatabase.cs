using CIS.Heartbeat;
using CIS.Management.Service.Persistence;
using CIS.Messaging.PostgreSql;
using CIS.Operations.PostgreSql;
using CIS.Startup;
using CIS.Transactions;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service;

internal sealed class ManagementDatabase(ILocalTransactionFactory transactions, ManagementServiceOptions options,
    PostgreSqlManagementStore store, IHeartbeatOwnerFactory heartbeat) : IStartable, IHeartbeatSubscriber, IAsyncDisposable
{
    private IHeartbeatOwner? owner;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await ValidateAsync(transactions, options, cancellationToken);
        IHeartbeatOwner candidate = heartbeat.CreateOwner();
        try
        {
            candidate.Service.Register(this, new()
            {
                Name = "management-telemetry-retention",
                Interval = TimeSpan.FromMinutes(1),
                InitialDelay = TimeSpan.FromMinutes(1)
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
        => (await store.CleanupAsync(options.Persistence.CleanupBatchSize, cancellationToken)).IsKnownCommitted;

    // Heartbeat owns the bounded error observation; no repeated module-level error stream is added.
    public Task OnErrorAsync(Exception exception) => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (owner is not null)
        {
            await owner.DisposeAsync();
        }
    }

    internal static async Task InstallAsync(ILocalTransactionFactory transactions, ManagementServiceOptions options,
        CancellationToken cancellationToken)
    {
        await using ILocalTransaction owner = await transactions.BeginAsync("management", cancellationToken);
        await ManagementSchema.InstallAsync(owner.Context, options.Persistence.Schema, options.Persistence, cancellationToken);
        await owner.Context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = 60;
            command.CommandText = PostgreSqlOperationSchema.GetInstallationSql(options.OperationsSchema);
            await command.ExecuteNonQueryAsync(token);
        }, cancellationToken);
        await PostgreSqlMessagingSchema.InstallAsync(owner.Context, options.MessagingSchema, cancellationToken);
        TransactionCompletion completion = await owner.CommitAsync(cancellationToken);
        if (completion.Outcome != TransactionOutcome.Committed || completion.CleanupErrors.Count != 0)
        {
            throw new ManagementSchemaException("Explicit management migration did not establish clean committed completion.");
        }
    }

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

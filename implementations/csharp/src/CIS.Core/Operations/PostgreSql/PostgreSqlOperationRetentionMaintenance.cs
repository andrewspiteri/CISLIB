using System.Data;
using System.Data.Common;
using System.Runtime.ExceptionServices;
using CIS.Operations.Hosting;
using CIS.Security;
using CIS.Transactions;
using CIS.Transactions.AdoNet;

namespace CIS.Operations.PostgreSql;

internal sealed class PostgreSqlOperationRetentionMaintenance(IServiceProvider services, OperationDefinition[] definitions,
    LocalApplicationIdentity application, ICapabilityAuthorizer authorizer, ICallerContextAccessor callers,
    ILocalTransactionFactory transactions, PostgreSqlOperationJournal journal) : IOperationRetentionMaintenance
{
    public async Task<OperationMaintenanceResult> RunAsync(OperationMaintenanceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        OperationDefinition definition = definitions.SingleOrDefault(item => item.Name == request.OperationName)
            ?? throw new ArgumentException("Use a registered operation alias.", nameof(request));
        cancellationToken.ThrowIfCancellationRequested();
        if (definition.MaintenanceCapabilities.Count == 0)
        {
            return new(OperationMaintenanceDisposition.Disabled);
        }
        IOperationScopeResolver resolver = definition.ScopeResolver(services)
            ?? throw new InvalidOperationException("The operation scope resolver factory returned no resolver.");
        OperationScope scope = await resolver.ResolveAsync(definition.Name, request.RequestedScope,
            callers.Current, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The operation resolver returned no trusted scope.");
        cancellationToken.ThrowIfCancellationRequested();
        var target = new CapabilityTarget(application.Application, application.Environment, scope.ResourceType, scope.ResourceIdentifier);
        AuthorizationDecision authorization = await authorizer.AuthorizeAsync(definition.MaintenanceCapabilities,
            target, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The capability authorizer returned no complete decision.");
        cancellationToken.ThrowIfCancellationRequested();
        if (!authorization.IsAllowed)
        {
            return new(OperationMaintenanceDisposition.Denied);
        }

        ILocalTransaction? owner = null;
        OperationMaintenanceResult? result = null;
        ExceptionDispatchInfo? primary = null;
        try
        {
            owner = await transactions.BeginAsync(definition.DatabaseName,
                new LocalTransactionOptions { Isolation = TransactionIsolation.ReadCommitted }, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The transaction factory returned no maintenance owner.");
            if (owner.Context.DatabaseName != definition.DatabaseName || owner.Context.Isolation != TransactionIsolation.ReadCommitted)
            {
                throw new InvalidOperationException("The maintenance owner must use the registered authoritative database and ReadCommitted isolation.");
            }
            (int cleared, int deleted) = await BatchAsync(owner.Context, definition.Name, scope.Scope,
                request.MaximumRows, cancellationToken).ConfigureAwait(false);
            TransactionCompletion completion = await owner.CommitAsync(cancellationToken).ConfigureAwait(false);
            result = new(OperationMaintenanceDisposition.Executed, cleared, deleted, completion);
        }
        catch (Exception error)
        {
            primary = ExceptionDispatchInfo.Capture(error);
        }
        finally
        {
            if (owner is not null)
            {
                try
                {
                    await owner.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception cleanup)
                {
                    if (result is null)
                    {
                        primary ??= ExceptionDispatchInfo.Capture(cleanup);
                    }
                    // A completion already published after native cleanup remains the batch's actual fact.
                }
            }
        }
        primary?.Throw();
        return result!;
    }

    private Task<(int Cleared, int Deleted)> BatchAsync(ILocalTransactionContext context, string operation, string scope,
        int maximum, CancellationToken cancellationToken) => context.UseAdoNetAsync(async (connection, transaction, token) =>
    {
        await journal.CheckVersionAsync(connection, transaction, token).ConfigureAwait(false);
        await using DbCommand command = journal.Command(connection, transaction, $"""
            WITH observation AS MATERIALIZED (SELECT clock_timestamp() AS observed_at),
            selected AS MATERIALIZED
            (
                SELECT journal.ctid AS tuple_id,
                    journal.protection_expires_at IS NOT NULL AND journal.protection_expires_at <= observation.observed_at AS delete_row
                FROM {journal.Settings.Journal} AS journal CROSS JOIN observation
                WHERE application = @application AND environment = @environment AND scope = @scope
                    AND operation_name = @operation AND finalized
                    AND
                    (
                        (protection_expires_at IS NOT NULL AND protection_expires_at <= observation.observed_at)
                        OR (result_expires_at IS NOT NULL AND result_expires_at <= observation.observed_at
                            AND (result_payload IS NOT NULL OR error_code IS NOT NULL OR error_message IS NOT NULL))
                    )
                ORDER BY idempotency_key COLLATE "C"
                LIMIT @maximum
                FOR UPDATE OF journal SKIP LOCKED
            ),
            removed AS
            (
                DELETE FROM {journal.Settings.Journal} AS journal USING selected
                WHERE journal.ctid = selected.tuple_id AND selected.delete_row
                RETURNING 1
            ),
            cleared AS
            (
                UPDATE {journal.Settings.Journal} AS journal
                SET result_payload = NULL, error_code = NULL, error_message = NULL
                FROM selected WHERE journal.ctid = selected.tuple_id AND NOT selected.delete_row
                RETURNING 1
            )
            SELECT (SELECT count(*) FROM cleared), (SELECT count(*) FROM removed)
            """);
        PostgreSqlOperationJournal.Parameter(command, "application", DbType.String, application.Application);
        PostgreSqlOperationJournal.Parameter(command, "environment", DbType.String, application.Environment);
        PostgreSqlOperationJournal.Parameter(command, "scope", DbType.String, scope);
        PostgreSqlOperationJournal.Parameter(command, "operation", DbType.String, operation);
        PostgreSqlOperationJournal.Parameter(command, "maximum", DbType.Int32, maximum);
        await using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            throw new InvalidOperationException("The operation maintenance batch returned no counts.");
        }
        int payloads = checked((int)reader.GetInt64(0));
        int deletions = checked((int)reader.GetInt64(1));
        if (payloads < 0 || deletions < 0 || payloads + deletions > maximum)
        {
            throw new InvalidOperationException("The operation maintenance batch exceeded its configured bound.");
        }
        return (payloads, deletions);
    }, cancellationToken);
}

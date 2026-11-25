using System.Data.Common;
using CIS.Management.Wire;
using CIS.Transactions;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Persistence;

public sealed partial class PostgreSqlManagementStore
{
    /// <summary>Uses exact PostgreSQL text predicates, never wildcard or locale case folding, before stable UUID paging.</summary>
    public Task<ManagementStoreResult<IReadOnlyList<ManagementApplication>>> QueryApplicationsAsync(ManagementApplicationQuery query,
        string? afterApplicationId, int take, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Query is not null && !ManagementWire.IsScalarText(query.Query, 128)
            || query.Environment is not null && !ManagementWire.IsScalarText(query.Environment, 255))
        {
            throw new ArgumentException("Supply bounded literal application and exact environment filters.", nameof(query));
        }
        Page(take);
        Guid after = afterApplicationId is null ? Guid.Empty : ManagementSql.Uuid(afterApplicationId, nameof(afterApplicationId));
        return RunAsync<IReadOnlyList<ManagementApplication>>(async (context, token) => await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
        {
            await using DbCommand command = ManagementSql.Command(connection, transaction, $"""
                SELECT application_id,application,environment,display_name,description,metadata_revision
                FROM {Table("management_application")}
                WHERE application_id>@after
                    AND (@environment IS NULL OR environment COLLATE "C"=@environment)
                    AND (@query IS NULL OR strpos(application,@query)>0 OR strpos(display_name,@query)>0)
                ORDER BY application_id LIMIT @take
                """, ("after", after), ("environment", query.Environment), ("query", query.Query), ("take", take));
            return Candidate<IReadOnlyList<ManagementApplication>>.Existing(await ReadDiscoveryApplications(command, sqlToken));
        }, token).ConfigureAwait(false), cancellationToken);
    }

    /// <summary>Provides bounded candidates; only the native authorization boundary can disclose environment names.</summary>
    public Task<ManagementStoreResult<IReadOnlyList<ManagementApplication>>> ReadEnvironmentCandidatesAsync(string? afterEnvironment,
        string? afterApplicationId, int take, CancellationToken cancellationToken = default)
    {
        Page(take);
        if (afterEnvironment is not null && !ManagementWire.IsScalarText(afterEnvironment, 255)
            || afterEnvironment is null && afterApplicationId is not null)
        {
            throw new ArgumentException("Supply an exact environment position.", nameof(afterEnvironment));
        }
        Guid after = afterApplicationId is null ? Guid.Empty : ManagementSql.Uuid(afterApplicationId, nameof(afterApplicationId));
        return RunAsync<IReadOnlyList<ManagementApplication>>(async (context, token) => await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
        {
            await using DbCommand command = ManagementSql.Command(connection, transaction, $"""
                SELECT application_id,application,environment,display_name,description,metadata_revision
                FROM {Table("management_application")}
                WHERE (@environment IS NULL OR environment COLLATE "C">@environment
                    OR (environment COLLATE "C"=@environment AND @resume AND application_id>@after))
                ORDER BY environment COLLATE "C",application_id LIMIT @take
                """, ("environment", afterEnvironment), ("after", after), ("resume", afterApplicationId is not null), ("take", take));
            return Candidate<IReadOnlyList<ManagementApplication>>.Existing(await ReadDiscoveryApplications(command, sqlToken));
        }, token).ConfigureAwait(false), cancellationToken);
    }

    internal Task<ManagementStoreResult<IReadOnlyList<ManagementConfigurationDefinition>>> ReadApplicationConfigurationDefinitionsAsync(
        string applicationId, string environment, string? afterSourceId, int take, CancellationToken cancellationToken)
    {
        Guid app = ManagementSql.Uuid(applicationId, nameof(applicationId));
        Guid after = afterSourceId is null ? Guid.Empty : ManagementSql.Uuid(afterSourceId, nameof(afterSourceId));
        Page(take);
        if (!ManagementWire.IsScalarText(environment, 255))
        {
            throw new ArgumentException("Supply the application's exact environment.", nameof(environment));
        }
        return RunCoreAsync(async (context, _, token) => Candidate<IReadOnlyList<ManagementConfigurationDefinition>>.Existing(
            await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
            {
                await RequireConfigurationCatalogAsync(connection, transaction, sqlToken);
                await using DbCommand command = ManagementSql.Command(connection, transaction, ConfigurationSourceSelect + $"""
                    WHERE s.enabled AND b.enabled AND s.application_id=@application AND s.environment COLLATE "C"=@environment
                        AND s.source_id>@after ORDER BY s.source_id LIMIT @take
                    """, ("application", app), ("environment", environment), ("after", after), ("take", take));
                await using DbDataReader reader = await command.ExecuteReaderAsync(sqlToken);
                var rows = new List<ManagementConfigurationDefinition>();
                while (await reader.ReadAsync(sqlToken))
                {
                    rows.Add(ReadConfigurationDefinition(reader) with
                    {
                        DefaultsJson = null
                    });
                }
                return (IReadOnlyList<ManagementConfigurationDefinition>)rows.AsReadOnly();
            }, token)), cancellationToken, requireReady: true, requireCleanRead: true);
    }

    private static async Task<IReadOnlyList<ManagementApplication>> ReadDiscoveryApplications(DbCommand command, CancellationToken token)
    {
        var rows = new List<ManagementApplication>();
        await using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            rows.Add(Application(reader));
        }
        return rows.AsReadOnly();
    }
}

using System.Data.Common;
using System.Globalization;
using System.Text;
using CIS.Management.Service.Configuration;
using CIS.Transactions;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Persistence;

public sealed partial class PostgreSqlManagementStore
{
    internal Task<ManagementStoreResult<ManagementConfigurationAssignmentMap>> ReadConfigurationAssignmentsAsync(
        ManagementConfigurationBindingResource binding, CancellationToken cancellationToken)
        => RunCoreAsync(async (context, authoritySnapshot, token) => await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
        {
            _ = await LockConfigurationQuotaAsync(connection, transaction, sqlToken, write: false);
            return Candidate<ManagementConfigurationAssignmentMap>.Existing(await ReadConfigurationMapAsync(connection, transaction, binding, sqlToken));
        }, token), cancellationToken, requireReady: true, requireCleanRead: true);

    internal Task<ConfigurationEffect> ReplaceConfigurationAssignmentsAsync(ILocalTransactionContext context, ManagementAssignmentInput input,
        CancellationToken cancellationToken)
    {
        ManagementSql.RequireContext(context);
        return context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            (long used, long quota) = await LockConfigurationQuotaAsync(connection, transaction, token);
            ManagementConfigurationBindingResource? binding = await LockConfigurationBindingAsync(connection, transaction, input.Binding.BindingId, token);
            if (binding is null || !binding.Enabled)
            {
                return ConfigurationEffect.Reject("management.configuration.binding_unavailable");
            }
            if (await IsRuleEngineConfigurationAsync(connection, transaction, binding.ApplicationId, binding.Environment, binding.ConfigurationId, token))
            {
                return ConfigurationEffect.Reject("management.configuration.rule_engine_managed");
            }
            ManagementConfigurationAssignmentMap before = await ReadConfigurationMapAsync(connection, transaction, binding, token, write: true);
            if (before.ETag != input.IfMatch)
            {
                return ConfigurationEffect.Reject("management.configuration.precondition_failed", before.ETag, before);
            }
            long revision = long.Parse(before.ETag["\"configuration.map.".Length..^1], CultureInfo.InvariantCulture);
            long generation = before.Generation is null ? 0 : long.Parse(before.Generation, CultureInfo.InvariantCulture);
            if (revision == long.MaxValue || generation == long.MaxValue)
            {
                return ConfigurationEffect.Reject("management.configuration.generation_exhausted", before.ETag, before);
            }
            try
            {
                await ValidateConfigurationAssignmentAsync(connection, transaction, binding, input.Command, token);
            }
            catch (ConfigurationReferenceException error)
            {
                return ConfigurationEffect.Reject(error.Code, before.ETag, before);
            }
            long previousOverrides;
            await using (DbCommand sum = ManagementSql.Command(connection, transaction,
                $"SELECT COALESCE(sum(logical_bytes),0)::bigint FROM {Table("configuration_override")} WHERE binding_id=@id", ("id", Guid.Parse(binding.BindingId))))
            {
                previousOverrides = (long)(await sum.ExecuteScalarAsync(token))!;
            }
            long nextOverrides = input.Command.Overrides.Sum(item => 96L + Encoding.UTF8.GetByteCount(item.CohortId));
            long delta = nextOverrides - previousOverrides;
            if (delta > quota - used)
            {
                return ConfigurationEffect.Reject("management.configuration.capacity_exceeded", before.ETag, before);
            }
            string timestamp = await ResetConfigurationAssignmentAsync(connection, transaction, Guid.Parse(binding.BindingId),
                generation + 1, Guid.Parse(input.Command.DefaultReleaseId), token);
            foreach (ManagementConfigurationOverride entry in input.Command.Overrides.OrderBy(item => item.CohortId, StringComparer.Ordinal))
            {
                await using DbCommand insert = ManagementSql.Command(connection, transaction, $"""
                    INSERT INTO {Table("configuration_override")}(binding_id,cohort_id,release_id,logical_bytes) VALUES(@binding,@cohort,@release,@bytes)
                    """, ("binding", Guid.Parse(binding.BindingId)), ("cohort", entry.CohortId), ("release", Guid.Parse(entry.ReleaseId)),
                    ("bytes", 96L + Encoding.UTF8.GetByteCount(entry.CohortId)));
                await insert.ExecuteNonQueryAsync(token);
            }
            await using DbCommand charge = ManagementSql.Command(connection, transaction,
                $"UPDATE {Table("configuration_catalog")} SET used_bytes=used_bytes+@delta WHERE singleton=true", ("delta", delta));
            await charge.ExecuteNonQueryAsync(token);
            ManagementConfigurationAssignmentMap after = await ReadConfigurationMapAsync(connection, transaction, binding, token);
            return new ConfigurationEffect(new(1, "assignmentsReplaced", "applied", "management.configuration.assignments_replaced", timestamp,
                ApplicationId: binding.ApplicationId, ConfigurationId: binding.ConfigurationId, SchemaVersion: binding.SchemaVersion,
                Generation: after.Generation, ETag: after.ETag, Intent: input.Command.Intent, Assignment: after),
                PreviousETag: before.ETag, BeforeAssignment: before);
        }, cancellationToken);
    }

    private async Task<string> ResetConfigurationAssignmentAsync(DbConnection connection, DbTransaction transaction,
        Guid binding, long generation, Guid release, CancellationToken token)
    {
        string timestamp;
        await using (DbCommand replace = ManagementSql.Command(connection, transaction, $"""
            UPDATE {Table("configuration_assignment")} SET revision=revision+1,generation=@generation,default_release_id=@release
            WHERE binding_id=@id RETURNING clock_timestamp()
            """, ("id", binding), ("generation", generation), ("release", release)))
        {
            timestamp = ManagementSql.Timestamp((DateTime)(await replace.ExecuteScalarAsync(token))!);
        }
        await using DbCommand remove = ManagementSql.Command(connection, transaction,
            $"DELETE FROM {Table("configuration_override")} WHERE binding_id=@id", ("id", binding));
        await remove.ExecuteNonQueryAsync(token);
        return timestamp;
    }

    private async Task ValidateConfigurationAssignmentAsync(DbConnection connection, DbTransaction transaction,
        ManagementConfigurationBindingResource binding, ManagementConfigurationAssignmentCommand command, CancellationToken token)
    {
        foreach (string id in command.Overrides.Select(item => item.ReleaseId).Append(command.DefaultReleaseId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            ManagementConfigurationRelease? release = await ReadConfigurationReleaseRowAsync(connection, transaction, Guid.Parse(id), token, protect: true);
            if (release is null)
            {
                throw new ConfigurationReferenceException("management.configuration.reference_not_found");
            }
            if (release.ApplicationId != binding.ApplicationId || release.ConfigurationId != binding.ConfigurationId
                || release.SchemaVersion != binding.SchemaVersion || release.Environment != binding.Environment)
            {
                throw new ConfigurationReferenceException("management.configuration.reference_scope");
            }
            ConfigurationSourcePair pair = await ReadConfigurationPairAsync(connection, transaction, binding, release.CommonSource, release.ApplicationSource, token);
            _ = ManagementConfigurationComposition.Compose(binding, pair);
            try
            {
                _ = WorstConfigurationEnvelopeBytes(pair);
            }
            catch (ArgumentException)
            {
                throw new ConfigurationReferenceException("management.configuration.response_too_large");
            }
        }
        foreach (ManagementConfigurationOverride entry in command.Overrides)
        {
            await using DbCommand known = ManagementSql.Command(connection, transaction, $"""
                SELECT EXISTS(SELECT 1 FROM {Table("configuration_cohort")} WHERE application_id=@application AND cohort_id=@cohort)
                    OR EXISTS(SELECT 1 FROM {Table("configuration_session")} WHERE application_id=@application AND cohort_id=@cohort)
                    OR EXISTS(SELECT 1 FROM {Table("configuration_override")} WHERE binding_id=@binding AND cohort_id=@cohort)
                """, ("application", Guid.Parse(binding.ApplicationId)), ("binding", Guid.Parse(binding.BindingId)), ("cohort", entry.CohortId));
            if (await known.ExecuteScalarAsync(token) is not true)
            {
                throw new ConfigurationReferenceException("management.configuration.cohort_unknown");
            }
        }
    }

    private async Task<ManagementConfigurationAssignmentMap> ReadConfigurationMapAsync(DbConnection connection, DbTransaction transaction,
        ManagementConfigurationBindingResource binding, CancellationToken token, bool write = false)
    {
        long revision;
        string? generation;
        string? release;
        await using (DbCommand select = ManagementSql.Command(connection, transaction,
            $"SELECT revision,generation,default_release_id FROM {Table("configuration_assignment")} WHERE binding_id=@id" + (write ? " FOR UPDATE" : ""),
            ("id", Guid.Parse(binding.BindingId))))
        await using (DbDataReader reader = await select.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token))
            {
                throw new ManagementSchemaException("The stable assignment map is absent.");
            }
            revision = reader.GetInt64(0);
            generation = reader.IsDBNull(1) ? null : reader.GetInt64(1).ToString(CultureInfo.InvariantCulture);
            release = reader.IsDBNull(2) ? null : reader.GetGuid(2).ToString("D");
        }
        await using DbCommand overrides = ManagementSql.Command(connection, transaction,
            $"SELECT cohort_id,release_id FROM {Table("configuration_override")} WHERE binding_id=@id ORDER BY cohort_id COLLATE \"C\"", ("id", Guid.Parse(binding.BindingId)));
        await using DbDataReader entries = await overrides.ExecuteReaderAsync(token);
        var items = new List<ManagementConfigurationOverride>();
        while (await entries.ReadAsync(token))
        {
            items.Add(new(entries.GetString(0), entries.GetGuid(1).ToString("D")));
        }
        return new(binding.BindingId, binding.ApplicationId, binding.ConfigurationId, binding.SchemaVersion, binding.Environment,
            ConfigurationETag("map", revision), items.AsReadOnly(), release, generation);
    }
}

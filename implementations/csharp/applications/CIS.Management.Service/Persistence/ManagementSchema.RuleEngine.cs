using System.Data.Common;

namespace CIS.Management.Service.Persistence;

public static partial class ManagementSchema
{
    internal const int RuleEngineReleaseHistoryBytes = 64;

    private static async Task InstallRuleEngineAsync(DbConnection connection, DbTransaction transaction,
        string schema, CancellationToken cancellationToken)
    {
        string Table(string name) => ManagementSql.Table(schema, name);

        await using DbCommand command = ManagementSql.Command(connection, transaction, $"""
            CREATE TABLE {Table("rule_engine_ruleset")} (
                ruleset_key uuid PRIMARY KEY,
                application_id uuid NOT NULL REFERENCES {Table("management_application")}(application_id),
                ruleset_id text COLLATE "C" NOT NULL,
                configuration_id text COLLATE "C" NOT NULL,
                active_schema_revision text COLLATE "C" NOT NULL,
                display_name text NOT NULL,
                enabled boolean NOT NULL,
                edit_revision bigint NOT NULL CHECK (edit_revision > 0),
                latest_version uuid NULL,
                UNIQUE (application_id,ruleset_id),
                UNIQUE (application_id,configuration_id));
            CREATE TABLE {Table("rule_engine_schema")} (
                ruleset_key uuid NOT NULL REFERENCES {Table("rule_engine_ruleset")}(ruleset_key),
                schema_revision text COLLATE "C" NOT NULL,
                definition bytea NOT NULL CHECK (octet_length(definition) BETWEEN 1 AND 131072),
                PRIMARY KEY (ruleset_key,schema_revision));
            CREATE TABLE {Table("rule_engine_version")} (
                ruleset_key uuid NOT NULL REFERENCES {Table("rule_engine_ruleset")}(ruleset_key),
                version_id uuid NOT NULL,
                schema_revision text COLLATE "C" NOT NULL,
                edit_revision bigint NOT NULL CHECK (edit_revision > 1),
                definition bytea NOT NULL CHECK (octet_length(definition) BETWEEN 1 AND 131072),
                saved_at timestamptz NOT NULL,
                PRIMARY KEY (ruleset_key,version_id),
                UNIQUE (ruleset_key,edit_revision),
                FOREIGN KEY (ruleset_key,schema_revision) REFERENCES {Table("rule_engine_schema")}(ruleset_key,schema_revision));
            CREATE TABLE {Table("rule_engine_identity")} (
                ruleset_key uuid NOT NULL REFERENCES {Table("rule_engine_ruleset")}(ruleset_key),
                rule_id text COLLATE "C" NOT NULL,
                PRIMARY KEY (ruleset_key,rule_id));
            """);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InstallRuleEngineDeploymentAsync(DbConnection connection, DbTransaction transaction,
        string schema, CancellationToken cancellationToken)
    {
        string Table(string name) => ManagementSql.Table(schema, name);

        await using DbCommand command = ManagementSql.Command(connection, transaction, $"""
            ALTER TABLE {Table("rule_engine_ruleset")} ADD COLUMN configuration_binding_id uuid NULL
                REFERENCES {Table("configuration_binding")}(binding_id);
            CREATE TABLE {Table("rule_engine_deployed_version")} (
                ruleset_key uuid NOT NULL,
                version_id uuid NOT NULL,
                first_assigned_at timestamptz NOT NULL,
                PRIMARY KEY (ruleset_key,version_id),
                FOREIGN KEY (ruleset_key,version_id) REFERENCES {Table("rule_engine_version")}(ruleset_key,version_id));
            CREATE TABLE {Table("rule_engine_deployment_release")} (
                release_id uuid PRIMARY KEY REFERENCES {Table("configuration_release")}(release_id) ON DELETE CASCADE,
                ruleset_key uuid NOT NULL,
                version_id uuid NOT NULL,
                UNIQUE (ruleset_key,version_id),
                FOREIGN KEY (ruleset_key,version_id) REFERENCES {Table("rule_engine_deployed_version")}(ruleset_key,version_id));
            """);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InstallRuleEngineReleaseHistoryAsync(DbConnection connection, DbTransaction transaction,
        string schema, CancellationToken cancellationToken)
    {
        string Table(string name) => ManagementSql.Table(schema, name);

        // Existing live links were charged inside the native release. Transfer that charge
        // to history without changing catalog usage so later cleanup only credits the carrier.
        await using DbCommand command = ManagementSql.Command(connection, transaction, $"""
            CREATE TABLE {Table("rule_engine_release_history")} (
                release_id uuid PRIMARY KEY,
                ruleset_key uuid NOT NULL,
                version_id uuid NOT NULL,
                FOREIGN KEY (ruleset_key,version_id) REFERENCES {Table("rule_engine_deployed_version")}(ruleset_key,version_id));
            INSERT INTO {Table("rule_engine_release_history")}(release_id,ruleset_key,version_id)
                SELECT release_id,ruleset_key,version_id FROM {Table("rule_engine_deployment_release")};
            UPDATE {Table("configuration_release")} r SET logical_bytes=r.logical_bytes-@history_bytes
                FROM {Table("rule_engine_release_history")} h WHERE r.release_id=h.release_id;
            """, ("history_bytes", RuleEngineReleaseHistoryBytes));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InstallRuleEngineVersionMetadataAsync(DbConnection connection, DbTransaction transaction,
        string schema, CancellationToken cancellationToken)
    {
        string Table(string name) => ManagementSql.Table(schema, name);

        await using DbCommand command = ManagementSql.Command(connection, transaction, $"""
            ALTER TABLE {Table("rule_engine_version")} ADD COLUMN version_note text NULL
                CHECK (octet_length(version_note) BETWEEN 1 AND 1024);
            ALTER TABLE {Table("rule_engine_ruleset")} ADD COLUMN deployment_requested_at timestamptz NULL,
                ADD COLUMN deployment_requested_generation bigint NULL CHECK (deployment_requested_generation > 0),
                ADD CHECK ((deployment_requested_at IS NULL) = (deployment_requested_generation IS NULL));
            """);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

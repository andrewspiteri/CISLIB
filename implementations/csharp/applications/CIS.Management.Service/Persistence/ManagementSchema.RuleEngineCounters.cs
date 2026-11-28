using System.Data.Common;

namespace CIS.Management.Service.Persistence;

public static partial class ManagementSchema
{
    private static async Task InstallRuleEngineCountersAsync(DbConnection connection, DbTransaction transaction,
        string schema, CancellationToken cancellationToken)
    {
        string Table(string name) => ManagementSql.Table(schema, name);
        await using DbCommand command = ManagementSql.Command(connection, transaction, $"""
            ALTER TABLE {Table("rule_engine_identity")} ADD COLUMN counter_visible_from_revision bigint NOT NULL DEFAULT 1
                CHECK(counter_visible_from_revision>0);
            CREATE TABLE {Table("rule_engine_counter_scope")} (
                ruleset_key uuid PRIMARY KEY REFERENCES {Table("rule_engine_ruleset")}(ruleset_key),
                reset_epoch bigint NOT NULL DEFAULT 0 CHECK(reset_epoch>=0),
                all_reset_at timestamptz NOT NULL,
                all_reset_revision bigint NOT NULL CHECK(all_reset_revision>0));
            CREATE TABLE {Table("rule_engine_counter")} (
                ruleset_key uuid NOT NULL,
                rule_id text COLLATE "C" NOT NULL,
                scope_epoch bigint NOT NULL CHECK(scope_epoch>=0),
                hit_count bigint NOT NULL CHECK(hit_count>=0),
                reset_at timestamptz NULL,
                PRIMARY KEY (ruleset_key,rule_id),
                FOREIGN KEY (ruleset_key,rule_id) REFERENCES {Table("rule_engine_identity")}(ruleset_key,rule_id));
            CREATE TABLE {Table("rule_engine_counter_execution")} (
                ruleset_key uuid NOT NULL,
                execution_id uuid NOT NULL,
                scope_epoch bigint NOT NULL CHECK(scope_epoch>=0),
                accepted_at timestamptz NOT NULL DEFAULT clock_timestamp(),
                PRIMARY KEY (ruleset_key,execution_id),
                FOREIGN KEY (ruleset_key,execution_id) REFERENCES {Table("rule_engine_execution")}(ruleset_key,execution_id));
            CREATE TABLE {Table("rule_engine_counter_reset")} (
                operation_scope text NOT NULL,
                operation_key text NOT NULL,
                ruleset_key uuid NOT NULL REFERENCES {Table("rule_engine_ruleset")}(ruleset_key),
                selection text NOT NULL CHECK(selection IN ('one','selected','all')),
                basis_revision bigint NOT NULL CHECK(basis_revision>0),
                outcomes bytea NULL CHECK(outcomes IS NULL OR octet_length(outcomes) BETWEEN 1 AND 65536),
                reset_at timestamptz NOT NULL,
                PRIMARY KEY(operation_scope,operation_key),
                CHECK ((selection='all')=(outcomes IS NULL)));
            """);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

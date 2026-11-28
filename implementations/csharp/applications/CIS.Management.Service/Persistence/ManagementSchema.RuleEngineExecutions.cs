using System.Data.Common;

namespace CIS.Management.Service.Persistence;

public static partial class ManagementSchema
{
    private static async Task InstallRuleEngineExecutionsAsync(DbConnection connection, DbTransaction transaction,
        string schema, CancellationToken cancellationToken)
    {
        string Table(string name) => ManagementSql.Table(schema, name);
        await using DbCommand command = ManagementSql.Command(connection, transaction, $"""
            CREATE TABLE {Table("rule_engine_execution")} (
                ruleset_key uuid NOT NULL,
                execution_id uuid NOT NULL,
                version_id uuid NOT NULL,
                producer_application text NOT NULL,
                instance_id text NOT NULL,
                registration_id uuid NOT NULL,
                application_build text NOT NULL,
                correlation_id text NULL,
                started_ticks bigint NOT NULL CHECK(started_ticks>0),
                finished_ticks bigint NULL,
                terminal_status text NULL CHECK(terminal_status IN ('Completed','Failed','Cancelled')),
                returned_match_count integer NULL CHECK(returned_match_count BETWEEN 0 AND 48),
                last_received_at timestamptz NOT NULL DEFAULT clock_timestamp(),
                logical_bytes bigint NOT NULL CHECK(logical_bytes>0),
                PRIMARY KEY (ruleset_key,execution_id),
                FOREIGN KEY (ruleset_key,version_id) REFERENCES {Table("rule_engine_deployed_version")}(ruleset_key,version_id),
                CHECK ((finished_ticks IS NULL)=(terminal_status IS NULL)),
                CHECK ((terminal_status IS NOT DISTINCT FROM 'Completed')=(returned_match_count IS NOT NULL)));
            CREATE INDEX rule_engine_execution_recency
                ON {Table("rule_engine_execution")}(ruleset_key,started_ticks DESC,execution_id DESC);
            CREATE INDEX rule_engine_execution_version
                ON {Table("rule_engine_execution")}(ruleset_key,version_id,started_ticks DESC,execution_id DESC);
            CREATE TABLE {Table("rule_engine_execution_event")} (
                ruleset_key uuid NOT NULL,
                execution_id uuid NOT NULL,
                phase smallint NOT NULL CHECK(phase IN (0,1)),
                audit_id uuid UNIQUE NOT NULL,
                causation_audit_id uuid NULL,
                body bytea NOT NULL CHECK(octet_length(body) BETWEEN 1 AND 131072),
                received_at timestamptz NOT NULL DEFAULT clock_timestamp(),
                PRIMARY KEY (ruleset_key,execution_id,phase),
                FOREIGN KEY (ruleset_key,execution_id) REFERENCES {Table("rule_engine_execution")}(ruleset_key,execution_id) ON DELETE CASCADE,
                CHECK ((phase=0)=(causation_audit_id IS NULL)));
            CREATE TABLE {Table("rule_engine_execution_rejection")} (
                audit_id uuid NOT NULL,
                payload_digest bytea NOT NULL CHECK(octet_length(payload_digest)=32),
                safe_code text NOT NULL,
                received_at timestamptz NOT NULL DEFAULT clock_timestamp(),
                PRIMARY KEY(audit_id,payload_digest));
            """);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

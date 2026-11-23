using CIS.Transactions;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Persistence;

/// <summary>Explicit application deployment migration and read-only schema readiness.</summary>
public static class ManagementSchema
{
    public const int Version = 1;

    /// <summary>Installs the version-one schema in the caller's original deployment transaction.</summary>
    public static Task InstallAsync(ILocalTransactionContext context, string schema,
        ManagementPersistenceOptions? options = null, CancellationToken cancellationToken = default)
    {
        ManagementSql.RequireContext(context);
        options ??= new(schema);
        if (options.Schema != schema)
        {
            throw new ArgumentException("Schema and captured persistence options must agree.", nameof(schema));
        }

        string T(string name) => ManagementSql.Table(schema, name);
        return context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            await using (var existing = ManagementSql.Command(connection, transaction, "SELECT to_regclass(@relation) IS NOT NULL",
                ("relation", T("management_schema_version"))))
            {
                object? relation = await existing.ExecuteScalarAsync(token).ConfigureAwait(false);
                if (relation is true)
                {
                    await using var version = ManagementSql.Command(connection, transaction,
                        $"SELECT COUNT(*)::integer,COALESCE(min(version),0) FROM {T("management_schema_version")}");
                    await using var reader = await version.ExecuteReaderAsync(token).ConfigureAwait(false);
                    if (!await reader.ReadAsync(token).ConfigureAwait(false) || reader.GetInt32(0) != 1 || reader.GetInt32(1) != Version)
                    {
                        throw new ManagementSchemaException("An existing management schema has an unsupported version.");
                    }
                }
            }

            await using var command = ManagementSql.Command(connection, transaction, $"""
                CREATE SCHEMA IF NOT EXISTS "{schema}";
                CREATE TABLE IF NOT EXISTS {T("management_schema_version")} (version integer PRIMARY KEY CHECK (version=1));
                INSERT INTO {T("management_schema_version")}(version) VALUES (1) ON CONFLICT DO NOTHING;
                CREATE TABLE IF NOT EXISTS {T("management_capacity")} (
                    singleton boolean PRIMARY KEY CHECK (singleton), application_count integer NOT NULL DEFAULT 0,
                    registration_count integer NOT NULL DEFAULT 0, inventory_bytes bigint NOT NULL DEFAULT 0,
                    CHECK(application_count>=0 AND registration_count>=0 AND inventory_bytes>=0));
                INSERT INTO {T("management_capacity")}(singleton) VALUES (true) ON CONFLICT (singleton) DO NOTHING;
                CREATE TABLE IF NOT EXISTS {T("management_application")} (
                    application_id uuid PRIMARY KEY, application text COLLATE "C" NOT NULL, environment text COLLATE "C" NOT NULL,
                    display_name text NOT NULL, description text NULL, metadata_revision bigint NOT NULL DEFAULT 1 CHECK(metadata_revision>0),
                    UNIQUE(application,environment));
                CREATE TABLE IF NOT EXISTS {T("management_registration")} (
                    registration_id uuid PRIMARY KEY, application_id uuid NOT NULL REFERENCES {T("management_application")}(application_id),
                    issuer text COLLATE "C" NOT NULL, subject text COLLATE "C" NOT NULL, registration_digest bytea NOT NULL,
                    registration_body bytea NOT NULL, application_version text NOT NULL, instance_evidence bytea NOT NULL, registered_at timestamptz NOT NULL,
                    observation_sequence bigint NULL CHECK(observation_sequence>0), observation_digest bytea NULL,
                    observation_body bytea NULL, observation_received_at timestamptz NULL,
                    inventory_sequence bigint NULL CHECK(inventory_sequence>0), inventory_digest bytea NULL,
                    inventory_body bytea NULL, inventory_bytes bigint NOT NULL DEFAULT 0 CHECK(inventory_bytes>=0),
                    inventory_received_at timestamptz NULL, closed_at timestamptz NULL);
                CREATE INDEX IF NOT EXISTS management_registration_application ON {T("management_registration")}(application_id,registration_id);
                """);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <summary>Reads the installed schema version; performs no migration or DDL during ordinary startup.</summary>
    public static Task ValidateAsync(ILocalTransactionContext context, string schema, CancellationToken cancellationToken = default)
    {
        ManagementSql.RequireContext(context);
        _ = new ManagementPersistenceOptions(schema);
        return context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            await using var command = ManagementSql.Command(connection, transaction,
                $"SELECT COUNT(*)::integer,COALESCE(min(version),0) FROM {ManagementSql.Table(schema, "management_schema_version")}");
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false) || reader.GetInt32(0) != 1 || reader.GetInt32(1) != Version)
            {
                throw new ManagementSchemaException("The management schema requires an explicit supported deployment migration.");
            }
        }, cancellationToken);
    }
}

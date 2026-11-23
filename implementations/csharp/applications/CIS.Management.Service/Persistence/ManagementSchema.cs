using CIS.Transactions;
using CIS.Transactions.AdoNet;
using CIS.Management.Service.Backends;

namespace CIS.Management.Service.Persistence;

/// <summary>Explicit application deployment migration and read-only schema readiness.</summary>
public static class ManagementSchema
{
    public const int Version = 2;

    /// <summary>Explicitly installs or upgrades version one without changing retained metadata, results or audit intent.</summary>
    public static Task InstallAsync(ILocalTransactionContext context, string schema,
        ManagementPersistenceOptions? options = null, CancellationToken cancellationToken = default,
        ManagementBackendOptions? backend = null)
    {
        ManagementSql.RequireContext(context);
        options ??= new(schema);
        backend ??= new();
        if (options.Schema != schema)
        {
            throw new ArgumentException("Schema and captured persistence options must agree.", nameof(schema));
        }

        string T(string name) => ManagementSql.Table(schema, name);
        return context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            int existingVersion = 0;
            await using (var existing = ManagementSql.Command(connection, transaction, "SELECT to_regclass(@relation) IS NOT NULL",
                ("relation", T("management_schema_version"))))
            {
                object? relation = await existing.ExecuteScalarAsync(token).ConfigureAwait(false);
                if (relation is true)
                {
                    await using var version = ManagementSql.Command(connection, transaction,
                        $"SELECT COUNT(*)::integer,COALESCE(min(version),0) FROM {T("management_schema_version")}");
                    await using var reader = await version.ExecuteReaderAsync(token).ConfigureAwait(false);
                    if (!await reader.ReadAsync(token).ConfigureAwait(false) || reader.GetInt32(0) != 1
                        || reader.GetInt32(1) is not (1 or Version))
                    {
                        throw new ManagementSchemaException("An existing management schema has an unsupported version.");
                    }
                    existingVersion = reader.GetInt32(1);
                }
            }
            if (existingVersion == Version)
            {
                // A missing/corrupt authority is recovery, not permission to mint a new namespace.
                await using var validate = ManagementSql.Command(connection, transaction,
                    $"SELECT namespace_name,recovery_epoch,authority_id,control_revision FROM {T("management_authority")} WHERE singleton=true");
                await using var reader = await validate.ExecuteReaderAsync(token).ConfigureAwait(false);
                if (!await reader.ReadAsync(token).ConfigureAwait(false) || reader.GetString(0) != backend.NamespaceName
                    || reader.GetInt64(1) != backend.ExpectedRecoveryEpoch || reader.GetGuid(2) == Guid.Empty
                    || !CIS.Management.Wire.ManagementWire.IsScalarText(reader.GetString(3), 256))
                {
                    throw new ManagementSchemaException("The installed authority must be reconciled explicitly, not reset by migration.");
                }
                return;
            }

            await using var command = ManagementSql.Command(connection, transaction, $"""
                CREATE SCHEMA IF NOT EXISTS "{schema}";
                CREATE TABLE IF NOT EXISTS {T("management_schema_version")} (version integer PRIMARY KEY CHECK (version=2));
                ALTER TABLE {T("management_schema_version")} DROP CONSTRAINT IF EXISTS management_schema_version_version_check;
                UPDATE {T("management_schema_version")} SET version=2;
                INSERT INTO {T("management_schema_version")}(version) VALUES (2) ON CONFLICT DO NOTHING;
                ALTER TABLE {T("management_schema_version")} ADD CONSTRAINT management_schema_version_version_check CHECK(version=2);
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
                CREATE TABLE {T("management_authority")} (
                    singleton boolean PRIMARY KEY CHECK(singleton), namespace_name text COLLATE "C" NOT NULL,
                    authority_id uuid NOT NULL CHECK(authority_id<>'00000000-0000-0000-0000-000000000000'::uuid),
                    control_revision text COLLATE "C" NOT NULL, recovery_epoch bigint NOT NULL CHECK(recovery_epoch>0),
                    recovery_hold boolean NOT NULL,
                    CHECK(char_length(namespace_name) BETWEEN 1 AND 128), CHECK(char_length(control_revision) BETWEEN 1 AND 256));
                INSERT INTO {T("management_authority")}(singleton,namespace_name,authority_id,control_revision,recovery_epoch,recovery_hold)
                    VALUES(true,@namespace,@authority,@revision,@epoch,@hold);
                """);
            foreach ((string name, object value) in new (string, object)[]
            {
                ("namespace", backend.NamespaceName), ("authority", Guid.NewGuid()),
                ("revision", "control." + Guid.NewGuid().ToString("D")), ("epoch", backend.ExpectedRecoveryEpoch), ("hold", backend.RecoveryHold)
            })
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value;
                command.Parameters.Add(parameter);
            }
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

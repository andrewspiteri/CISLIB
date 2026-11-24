namespace CIS.Management.Service.Persistence;

// Complete version-three layout. Later stories add behavior over these already installed tables.
internal static class ManagementConfigurationSchemaSql
{
    internal static string Installation(string schema)
    {
        string T(string name) => ManagementSql.Table(schema, name);
        return $"""
            CREATE TABLE {T("configuration_catalog")} (
                singleton boolean PRIMARY KEY CHECK(singleton), catalog_id uuid NOT NULL UNIQUE,
                revision bigint NOT NULL DEFAULT 1 CHECK(revision>0), manifest_digest text NULL,
                quota_bytes bigint NULL CHECK(quota_bytes>0), used_bytes bigint NOT NULL DEFAULT 0 CHECK(used_bytes>=0),
                retained_releases integer NULL CHECK(retained_releases>=0), pins_body bytea NULL,
                metadata_bytes bigint NOT NULL DEFAULT 0 CHECK(metadata_bytes>=0),
                CHECK((manifest_digest IS NULL)=(quota_bytes IS NULL)), CHECK(quota_bytes IS NULL OR used_bytes<=quota_bytes));
            INSERT INTO {T("configuration_catalog")}(singleton,catalog_id) VALUES(true,@catalog_id);
            CREATE TABLE {T("configuration_definition")} (
                definition_id uuid PRIMARY KEY, configuration_id text COLLATE "C" NOT NULL,
                schema_version bigint NOT NULL CHECK(schema_version>0), definition_body bytea NOT NULL,
                definition_digest bytea NOT NULL CHECK(octet_length(definition_digest)=32), logical_bytes bigint NOT NULL CHECK(logical_bytes>0),
                enabled boolean NOT NULL DEFAULT true,
                UNIQUE(configuration_id,schema_version));
            CREATE TABLE {T("configuration_binding")} (
                binding_id uuid PRIMARY KEY, application_id uuid NOT NULL REFERENCES {T("management_application")}(application_id),
                definition_id uuid NOT NULL REFERENCES {T("configuration_definition")}(definition_id), host_scope text COLLATE "C" NOT NULL,
                enabled boolean NOT NULL DEFAULT true, logical_bytes bigint NOT NULL CHECK(logical_bytes>0), UNIQUE(application_id,definition_id));
            CREATE TABLE {T("configuration_source")} (
                source_id uuid PRIMARY KEY, definition_id uuid NOT NULL REFERENCES {T("configuration_definition")}(definition_id),
                environment text COLLATE "C" NOT NULL, application_id uuid NULL REFERENCES {T("management_application")}(application_id),
                binding_id uuid NULL REFERENCES {T("configuration_binding")}(binding_id), revision bigint NOT NULL DEFAULT 1 CHECK(revision>0),
                enabled boolean NOT NULL DEFAULT true, logical_bytes bigint NOT NULL CHECK(logical_bytes>0),
                CHECK((application_id IS NULL)=(binding_id IS NULL)), UNIQUE NULLS NOT DISTINCT(definition_id,environment,application_id));
            CREATE INDEX configuration_source_binding ON {T("configuration_source")}(binding_id);
            CREATE TABLE {T("configuration_publication")} (
                publication_id uuid PRIMARY KEY, source_id uuid NOT NULL REFERENCES {T("configuration_source")}(source_id),
                payload bytea NOT NULL, sha256 text COLLATE "C" NOT NULL CHECK(length(sha256)=64),
                actor text NOT NULL, created_at timestamptz NOT NULL, reason text NOT NULL,
                logical_bytes bigint NOT NULL CHECK(logical_bytes>0));
            CREATE INDEX configuration_publication_source ON {T("configuration_publication")}(source_id,publication_id);
            CREATE TABLE {T("configuration_release")} (
                release_id uuid PRIMARY KEY, binding_id uuid NOT NULL REFERENCES {T("configuration_binding")}(binding_id),
                common_publication_id uuid NULL REFERENCES {T("configuration_publication")}(publication_id) ON DELETE RESTRICT,
                application_publication_id uuid NULL REFERENCES {T("configuration_publication")}(publication_id) ON DELETE RESTRICT,
                actor text NOT NULL, created_at timestamptz NOT NULL, reason text NOT NULL,
                encoded_bytes bigint NOT NULL CHECK(encoded_bytes>0), logical_bytes bigint NOT NULL CHECK(logical_bytes>0), pinned boolean NOT NULL DEFAULT false);
            CREATE INDEX configuration_release_binding ON {T("configuration_release")}(binding_id,created_at,release_id);
            CREATE TABLE {T("configuration_assignment")} (
                binding_id uuid PRIMARY KEY REFERENCES {T("configuration_binding")}(binding_id), revision bigint NOT NULL DEFAULT 1 CHECK(revision>0),
                generation bigint NULL CHECK(generation>0), default_release_id uuid NULL REFERENCES {T("configuration_release")}(release_id) ON DELETE RESTRICT,
                logical_bytes bigint NOT NULL CHECK(logical_bytes>0), CHECK((generation IS NULL)=(default_release_id IS NULL)));
            CREATE TABLE {T("configuration_override")} (
                binding_id uuid NOT NULL REFERENCES {T("configuration_assignment")}(binding_id), cohort_id text COLLATE "C" NOT NULL,
                release_id uuid NOT NULL REFERENCES {T("configuration_release")}(release_id) ON DELETE RESTRICT,
                logical_bytes bigint NOT NULL CHECK(logical_bytes>0), PRIMARY KEY(binding_id,cohort_id));
            CREATE TABLE {T("configuration_cohort")} (
                application_id uuid NOT NULL REFERENCES {T("management_application")}(application_id), issuer text COLLATE "C" NOT NULL,
                subject text COLLATE "C" NOT NULL, cohort_id text COLLATE "C" NOT NULL, logical_bytes bigint NOT NULL CHECK(logical_bytes>0),
                PRIMARY KEY(application_id,issuer,subject));
            CREATE TABLE {T("configuration_session")} (
                registration_id uuid PRIMARY KEY REFERENCES {T("management_registration")}(registration_id) ON DELETE CASCADE,
                application_id uuid NOT NULL REFERENCES {T("management_application")}(application_id), cohort_id text COLLATE "C" NULL,
                catalog_revision bigint NOT NULL CHECK(catalog_revision>0), supported_body bytea NOT NULL, logical_bytes bigint NOT NULL CHECK(logical_bytes>0));
            CREATE TABLE {T("configuration_adoption")} (
                registration_id uuid PRIMARY KEY REFERENCES {T("configuration_session")}(registration_id) ON DELETE CASCADE,
                sequence bigint NOT NULL CHECK(sequence>0), digest bytea NOT NULL CHECK(octet_length(digest)=32), body bytea NOT NULL,
                observed_at timestamptz NOT NULL, received_at timestamptz NOT NULL, logical_bytes bigint NOT NULL CHECK(logical_bytes>0));
            """;
    }
}

-- CIS.Coordination v1. One atomic installation, explicitly invoked by deployment.
-- Never recreate or reset the allocator or authority identity to repair an existing installation.
DO $cis_coordination$
BEGIN
    IF current_setting('server_encoding') <> 'UTF8'
        OR current_setting('block_size')::integer < 8192
        OR current_setting('server_version_num')::integer < 180000 THEN
        RAISE EXCEPTION 'CIS.Coordination v1 requires PostgreSQL 18+, UTF8 and block_size >= 8192';
    END IF;

    CREATE SCHEMA IF NOT EXISTS __CIS_COORDINATION_SCHEMA__;

    CREATE TABLE __CIS_COORDINATION_SCHEMA__.authority_metadata
    (
        singleton boolean PRIMARY KEY CHECK (singleton),
        version integer NOT NULL CHECK (version = 1),
        authority_id uuid NOT NULL CHECK (authority_id <> '00000000-0000-0000-0000-000000000000'::uuid)
    );

    CREATE SEQUENCE __CIS_COORDINATION_SCHEMA__.grant_generation
        AS bigint INCREMENT BY 1 MINVALUE 1 MAXVALUE 9223372036854775807
        START WITH 1 CACHE 1 NO CYCLE OWNED BY NONE;

    CREATE TABLE __CIS_COORDINATION_SCHEMA__.ownership_grant
    (
        environment text COLLATE "C" NOT NULL CHECK (octet_length(environment) BETWEEN 1 AND 1024),
        execution_scope text COLLATE "C" NOT NULL CHECK (octet_length(execution_scope) BETWEEN 1 AND 256),
        duty_id text COLLATE "C" NOT NULL CHECK (octet_length(duty_id) BETWEEN 1 AND 128),
        resource_id text COLLATE "C" NOT NULL CHECK (octet_length(resource_id) BETWEEN 1 AND 512),
        owner_application text COLLATE "C" NULL CHECK (octet_length(owner_application) BETWEEN 1 AND 1024),
        instance_id text COLLATE "C" NULL CHECK (octet_length(instance_id) BETWEEN 1 AND 128),
        acquisition_id uuid NULL CHECK (acquisition_id <> '00000000-0000-0000-0000-000000000000'::uuid),
        generation bigint NULL CHECK (generation > 0),
        expires_at timestamptz NULL,
        released boolean NOT NULL DEFAULT true,
        PRIMARY KEY (environment, execution_scope, duty_id, resource_id),
        CHECK
        (
            (owner_application IS NULL AND instance_id IS NULL AND acquisition_id IS NULL
                AND generation IS NULL AND expires_at IS NULL AND released)
            OR
            (owner_application IS NOT NULL AND instance_id IS NOT NULL AND acquisition_id IS NOT NULL
                AND generation IS NOT NULL AND expires_at IS NOT NULL)
        )
    );

    INSERT INTO __CIS_COORDINATION_SCHEMA__.authority_metadata (singleton, version, authority_id)
    VALUES (true, 1, '__CIS_COORDINATION_AUTHORITY__'::uuid);
END
$cis_coordination$;

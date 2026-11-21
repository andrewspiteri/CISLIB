-- CIS.Operations PostgreSQL schema v1. Execute explicitly; registration never installs it.
-- __CIS_OPERATIONS_SCHEMA__ is one quoted identifier substituted by GetInstallationSql.
DO $cis_operations$
BEGIN
    IF current_setting('server_encoding') <> 'UTF8' THEN
        RAISE EXCEPTION 'CIS.Operations v1 requires UTF8 server encoding';
    END IF;
    IF current_setting('block_size')::integer < 8192 THEN
        RAISE EXCEPTION 'CIS.Operations v1 requires PostgreSQL block_size >= 8192';
    END IF;
    IF current_setting('server_version_num')::integer < 180000 THEN
        RAISE EXCEPTION 'CIS.Operations v1 requires PostgreSQL 18 or later';
    END IF;
END
$cis_operations$;

CREATE SCHEMA IF NOT EXISTS __CIS_OPERATIONS_SCHEMA__;

CREATE TABLE __CIS_OPERATIONS_SCHEMA__.schema_version
(
    singleton boolean PRIMARY KEY CHECK (singleton),
    version integer NOT NULL
);

INSERT INTO __CIS_OPERATIONS_SCHEMA__.schema_version (singleton, version)
VALUES (true, 1);

DO $cis_operations$
BEGIN
    IF (SELECT version FROM __CIS_OPERATIONS_SCHEMA__.schema_version WHERE singleton) IS DISTINCT FROM 1 THEN
        RAISE EXCEPTION 'Unsupported CIS.Operations schema version';
    END IF;
END
$cis_operations$;

CREATE TABLE __CIS_OPERATIONS_SCHEMA__.operation_journal
(
    application text COLLATE "C" NOT NULL CHECK (char_length(application) BETWEEN 1 AND 255 AND octet_length(application) <= 1020),
    environment text COLLATE "C" NOT NULL CHECK (char_length(environment) BETWEEN 1 AND 255 AND octet_length(environment) <= 1020),
    scope text COLLATE "C" NOT NULL CHECK (octet_length(scope) BETWEEN 1 AND 256),
    operation_name text COLLATE "C" NOT NULL CHECK (operation_name ~ '^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$'),
    idempotency_key text COLLATE "C" NOT NULL CHECK (idempotency_key ~ '^[!-~]{1,128}$'),
    fingerprint_contract text COLLATE "C" NOT NULL CHECK (fingerprint_contract ~ '^[A-Za-z0-9][A-Za-z0-9_.:/-]{0,127}$'),
    fingerprint_version integer NOT NULL CHECK (fingerprint_version > 0),
    fingerprint bytea NOT NULL CHECK (octet_length(fingerprint) = 32),
    claim_xid xid8 NOT NULL DEFAULT pg_current_xact_id(),
    finalized boolean NOT NULL DEFAULT false,
    result_contract text COLLATE "C" NULL CHECK (result_contract ~ '^[A-Za-z0-9][A-Za-z0-9_.:/-]{0,127}$'),
    result_version integer NULL CHECK (result_version > 0),
    business_decision smallint NULL CHECK (business_decision IN (0, 1)),
    result_payload bytea NULL CHECK (octet_length(result_payload) <= 1048576),
    error_code text NULL CHECK (char_length(error_code) BETWEEN 1 AND 128 AND error_code ~ '^[ -~]+$'),
    error_message text NULL CHECK (octet_length(error_message) BETWEEN 1 AND 1024),
    recorded_at timestamptz NULL,
    result_expires_at timestamptz NULL,
    protection_expires_at timestamptz NULL,
    PRIMARY KEY (application, environment, scope, operation_name, idempotency_key),
    CHECK (protection_expires_at IS NULL OR (result_expires_at IS NOT NULL AND protection_expires_at >= result_expires_at)),
    CHECK (result_expires_at IS NULL OR result_expires_at > recorded_at),
    CHECK
    (
        (NOT finalized AND result_contract IS NULL AND result_version IS NULL AND business_decision IS NULL
            AND result_payload IS NULL AND error_code IS NULL AND error_message IS NULL AND recorded_at IS NULL
            AND result_expires_at IS NULL AND protection_expires_at IS NULL)
        OR
        (finalized AND result_contract IS NOT NULL AND result_version IS NOT NULL AND business_decision IS NOT NULL
            AND recorded_at IS NOT NULL AND
            (
                (business_decision = 0 AND result_payload IS NOT NULL AND error_code IS NULL AND error_message IS NULL)
                OR (business_decision = 1 AND result_payload IS NULL AND error_code IS NOT NULL AND error_message IS NOT NULL)
                OR (result_expires_at IS NOT NULL AND result_payload IS NULL AND error_code IS NULL AND error_message IS NULL)
            ))
    )
);

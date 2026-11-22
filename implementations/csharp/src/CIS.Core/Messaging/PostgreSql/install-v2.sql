-- CIS.Messaging PostgreSQL schema v2. Execute explicitly once, with deployment privileges.
-- No transaction control, migration, implicit repair, extension or Operations schema dependency.
DO $cis_messaging$
BEGIN
    IF current_setting('server_version_num')::integer < 180000
        OR current_setting('server_encoding') <> 'UTF8'
        OR current_setting('block_size')::integer < 8192 THEN
        RAISE EXCEPTION 'CIS.Messaging v2 requires PostgreSQL 18+, UTF8 and block_size >= 8192';
    END IF;
END
$cis_messaging$;

CREATE SCHEMA IF NOT EXISTS __CIS_MESSAGING_SCHEMA__;
CREATE TABLE __CIS_MESSAGING_SCHEMA__.schema_version
(
    singleton boolean PRIMARY KEY CHECK (singleton),
    version integer NOT NULL CHECK (version = 2)
);
INSERT INTO __CIS_MESSAGING_SCHEMA__.schema_version VALUES (true, 2);

CREATE TABLE __CIS_MESSAGING_SCHEMA__.storage_budget
(
    application text COLLATE "C" NOT NULL CHECK (char_length(application) BETWEEN 1 AND 255 AND octet_length(application) <= 1020),
    environment text COLLATE "C" NOT NULL CHECK (char_length(environment) BETWEEN 1 AND 255 AND octet_length(environment) <= 1020),
    maximum_bytes bigint NULL CHECK (maximum_bytes > 0),
    retained_bytes bigint NOT NULL DEFAULT 0 CHECK (retained_bytes >= 0 AND (maximum_bytes IS NULL OR retained_bytes <= maximum_bytes)),
    PRIMARY KEY (application, environment)
);

CREATE TABLE __CIS_MESSAGING_SCHEMA__.processing
(
    record_id uuid NOT NULL UNIQUE,
    application text COLLATE "C" NOT NULL CHECK (char_length(application) BETWEEN 1 AND 255 AND octet_length(application) <= 1020),
    environment text COLLATE "C" NOT NULL CHECK (char_length(environment) BETWEEN 1 AND 255 AND octet_length(environment) <= 1020),
    subscription text COLLATE "C" NOT NULL CHECK (subscription ~ '^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$'),
    message_id uuid NOT NULL,
    digest bytea NOT NULL CHECK (octet_length(digest) = 32),
    envelope bytea NULL CHECK (octet_length(envelope) BETWEEN 1 AND 1064960),
    provenance text NOT NULL CHECK (octet_length(provenance) BETWEEN 1 AND 32768),
    operation_binding text NULL CHECK (octet_length(operation_binding) BETWEEN 1 AND 65536),
    protection_retention_micros bigint NULL CHECK (protection_retention_micros > 0),
    terminal_retention_micros bigint NULL CHECK (terminal_retention_micros > 0),
    payload_retention_micros bigint NULL CHECK (payload_retention_micros > 0),
    redrive_horizon_micros bigint NULL CHECK (redrive_horizon_micros > 0),
    delivery_horizon_micros bigint NULL CHECK (delivery_horizon_micros > 0),
    payload_expires timestamptz NULL,
    ordinal integer NOT NULL CHECK (ordinal BETWEEN 1 AND 32),
    generation integer NOT NULL DEFAULT 0 CHECK (generation >= 0),
    state smallint NOT NULL DEFAULT 0 CHECK (state BETWEEN 0 AND 3),
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    claim_xid xid8 NOT NULL DEFAULT pg_current_xact_id(),
    handoff_id uuid NULL,
    reply_id uuid NULL,
    safe_code text NULL CHECK (octet_length(safe_code) BETWEEN 1 AND 128),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    protection_expires timestamptz NULL,
    terminal_expires timestamptz NULL,
    PRIMARY KEY (application, environment, subscription, message_id),
    CHECK (state <> 2 OR handoff_id IS NOT NULL),
    CHECK (state <> 3 OR handoff_id IS NOT NULL)
);

CREATE TABLE __CIS_MESSAGING_SCHEMA__.outbox
(
    record_id uuid PRIMARY KEY,
    application text COLLATE "C" NOT NULL CHECK (char_length(application) BETWEEN 1 AND 255 AND octet_length(application) <= 1020),
    environment text COLLATE "C" NOT NULL CHECK (char_length(environment) BETWEEN 1 AND 255 AND octet_length(environment) <= 1020),
    message_id uuid NOT NULL,
    route_alias text COLLATE "C" NOT NULL CHECK (route_alias ~ '^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$'),
    target_subscription text COLLATE "C" NULL CHECK (target_subscription ~ '^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$'),
    reply_application text COLLATE "C" NULL,
    reply_environment text COLLATE "C" NULL,
    reply_endpoint uuid NULL,
    reply_to_message_id uuid NULL,
    purpose smallint NOT NULL CHECK (purpose BETWEEN 0 AND 5),
    digest bytea NOT NULL CHECK (octet_length(digest) = 32),
    envelope bytea NULL CHECK (octet_length(envelope) BETWEEN 1 AND 1064960),
    encoded_length integer NOT NULL CHECK (encoded_length BETWEEN 1 AND 1064960),
    processing_application text COLLATE "C" NULL,
    processing_environment text COLLATE "C" NULL,
    subscription text COLLATE "C" NULL,
    original_message_id uuid NULL,
    rejection_id uuid NULL,
    staged_xid xid8 NOT NULL DEFAULT pg_current_xact_id(),
    ordinal integer NOT NULL CHECK (ordinal BETWEEN 1 AND 32),
    generation integer NOT NULL DEFAULT 0 CHECK (generation >= 0),
    provenance text NULL CHECK (octet_length(provenance) BETWEEN 1 AND 32768),
    state smallint NOT NULL DEFAULT 0 CHECK (state BETWEEN 0 AND 3),
    eligible_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    reply_expired boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    published_at timestamptz NULL,
    payload_retention_micros bigint NULL CHECK (payload_retention_micros > 0),
    payload_expires timestamptz NULL,
    attempts integer NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    reservation_owner uuid NULL,
    reservation_version bigint NOT NULL DEFAULT 0 CHECK (reservation_version >= 0),
    reserved_until timestamptz NULL,
    safe_code text NULL CHECK (octet_length(safe_code) BETWEEN 1 AND 128),
    FOREIGN KEY (processing_application, processing_environment, subscription, original_message_id)
        REFERENCES __CIS_MESSAGING_SCHEMA__.processing(application, environment, subscription, message_id) ON DELETE SET NULL,
    CHECK ((reply_application IS NULL AND reply_environment IS NULL AND reply_endpoint IS NULL)
        OR (reply_application IS NOT NULL AND reply_environment IS NOT NULL AND reply_endpoint IS NOT NULL)),
    CHECK ((processing_application IS NULL AND processing_environment IS NULL AND subscription IS NULL AND original_message_id IS NULL)
        OR (processing_application IS NOT NULL AND processing_environment IS NOT NULL AND subscription IS NOT NULL AND original_message_id IS NOT NULL)),
    CHECK (processing_application IS NULL OR rejection_id IS NULL),
    CHECK (rejection_id IS NULL OR purpose=3),
    CHECK ((purpose=3) = (reply_to_message_id IS NOT NULL)),
    CHECK (purpose<>3 OR state=2 OR reply_expired OR ((processing_application IS NOT NULL) <> (rejection_id IS NOT NULL))),
    CHECK (state <> 1 OR (reservation_owner IS NOT NULL AND reserved_until IS NOT NULL)),
    CHECK (NOT reply_expired OR (purpose=3 AND state=3 AND safe_code IS NOT NULL AND safe_code='messaging.reply_destination_expired')),
    CHECK (state = 2 OR reply_expired OR envelope IS NOT NULL),
    CHECK (envelope IS NULL OR octet_length(envelope) = encoded_length)
);
CREATE UNIQUE INDEX outbox_handoff_identity ON __CIS_MESSAGING_SCHEMA__.outbox
    (processing_application, processing_environment, subscription, original_message_id, purpose, generation, ordinal)
    WHERE processing_application IS NOT NULL;
CREATE INDEX outbox_admission ON __CIS_MESSAGING_SCHEMA__.outbox(application, environment, eligible_at, record_id)
    WHERE state IN (0, 1);
CREATE INDEX outbox_backlog ON __CIS_MESSAGING_SCHEMA__.outbox(application, environment, created_at, record_id)
    WHERE state <> 2 AND NOT reply_expired;
ALTER TABLE __CIS_MESSAGING_SCHEMA__.processing ADD FOREIGN KEY (handoff_id) REFERENCES __CIS_MESSAGING_SCHEMA__.outbox(record_id);
ALTER TABLE __CIS_MESSAGING_SCHEMA__.processing ADD FOREIGN KEY (reply_id) REFERENCES __CIS_MESSAGING_SCHEMA__.outbox(record_id);

CREATE TABLE __CIS_MESSAGING_SCHEMA__.rejections
(
    record_id uuid PRIMARY KEY,
    application text COLLATE "C" NOT NULL CHECK (char_length(application) BETWEEN 1 AND 255 AND octet_length(application) <= 1020),
    environment text COLLATE "C" NOT NULL CHECK (char_length(environment) BETWEEN 1 AND 255 AND octet_length(environment) <= 1020),
    subscription text COLLATE "C" NOT NULL CHECK (subscription ~ '^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$'),
    message_id uuid NOT NULL,
    digest bytea NOT NULL CHECK (octet_length(digest) = 32),
    provenance text NULL CHECK (octet_length(provenance) BETWEEN 1 AND 32768),
    handoff_id uuid NOT NULL REFERENCES __CIS_MESSAGING_SCHEMA__.outbox(record_id),
    reply_id uuid NULL REFERENCES __CIS_MESSAGING_SCHEMA__.outbox(record_id),
    created_xid xid8 NOT NULL DEFAULT pg_current_xact_id(),
    safe_code text NOT NULL CHECK (octet_length(safe_code) BETWEEN 1 AND 128),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    terminal_expires timestamptz NULL
);
ALTER TABLE __CIS_MESSAGING_SCHEMA__.outbox ADD FOREIGN KEY (rejection_id)
    REFERENCES __CIS_MESSAGING_SCHEMA__.rejections(record_id) ON DELETE SET NULL;
CREATE UNIQUE INDEX outbox_rejection_reply ON __CIS_MESSAGING_SCHEMA__.outbox(rejection_id) WHERE rejection_id IS NOT NULL;

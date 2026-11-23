# Central management service

This is the independently deployed RM-07 product application. It composes the
existing `CISApplication`, Security, Transactions, Operations, Messaging and Audit
contracts. CountryData remains the maintained tutorial application.

Use the [local/container deployment guide](deployment/README.md) for the pinned
Linux image, external dependencies, secret/trust references and separate
migration/topology commands. The [module guide](../../../../docs/modules/management/README.md)
links both clients and optional process reporting.

The service uses one native provider and sequential Startup. Registration and
constructors perform no database, broker or credential I/O. Server certificate
preparation and read-only schema checks finish before readiness and listeners.
The service does not register itself with management during its own startup.

## Bootstrap and schema

Set `CORE_SERVICE_NAME` and `CORE_ENVIRONMENT` to the management service's actual
identity. They identify the capability target; a managed application's reported
name cannot replace them. Supply the PostgreSQL connection through the secret
environment variable `CIS_MANAGEMENT_POSTGRES` and a deployment-shared base64
32 through 64 byte cursor signing key through `CIS_MANAGEMENT_CURSOR_KEY`.
`DatabaseConnectionEnvironment` and `CursorKeyEnvironment` may select other
bounded environment-variable names. Never put these values in committed files.

Use `--settings=<absolute-json-file>` for trusted public bootstrap settings.
Environment variables and command-line settings override that startup snapshot;
there is no live trust or grants reload. An optional literal `BasePath`, such as
`/platform`, prefixes every `/management/v1` route. It contains only safe path
segments and is not a destination supplied by a request.

Run the explicit `--migrate` mode with deployment credentials to install the
application and existing Operations/Messaging schemas in one local transaction.
This is a deployment action, separate from serving traffic. Ordinary startup
only checks the supported installed versions; missing or unsupported schemas
prevent readiness. Do not give ordinary service credentials schema-changing
permissions. Foundation installation scripts are versioned deployment scripts,
not an automatic startup migration loop.

The application-owned Npgsql data source permits four pooled connections per
replica. It stays alive through real native transaction and host drain. Existing
operation protection and application metadata are retained independently of
bounded registration/inventory retention.

Serving traffic also requires explicit `Delivery.Broker` settings for the
existing RabbitMQ adapter. Supply the broker password through
`CIS_MANAGEMENT_RABBIT_PASSWORD` (or its configured `PasswordEnvironment`
reference), and select the actual hostname, TLS server name, virtual host,
publishing user and owned namespace. Public `TrustedRootCertificatePem` is a
bounded immutable trust input; omission selects native system trust. Credentials
are not copied into API responses or diagnostic fields.

The native broker profile is separate from business authentication and receives
no management administrative grant. The existing Messaging runtime owns real
broker Startup and outgoing delivery; unavailable selected prerequisites fail
truthfully. After startup, `RequireLiveBrokerForReadiness` selects the existing
Messaging readiness behavior. Committed Audit records remain the outbox's
responsibility during later broker outages. Default retained envelope capacity
is 64 MiB, with a supported ceiling of 1 GiB; required staging refusal rolls back
the metadata transaction. This application publishes Audit events and supplies
no Audit destination database or archive.

Provision the selected durable broker graph explicitly with `--install-topology`
before serving traffic. This mode uses the distinct `Delivery.Broker.SetupUserName`
and `SetupPasswordEnvironment` reference (default
`CIS_MANAGEMENT_RABBIT_SETUP_PASSWORD`); it does not load the publishing password,
database credential, cursor key or server private key. The actual provider installs
the same finite Audit graph that ordinary startup only validates. It does not
delete or replace existing resources. SQL migration and broker provisioning are
separate deployment actions; ordinary startup receives the narrower publishing
credential. Neither deployment action runs as part of handling requests.

## Native security and listeners

`Security.Mode` explicitly selects `local`, `certificate`, `entra` or a compiled
`native` integration. LocalDevelopment is isolated and loopback-only. Production
certificate and Entra selections retain their actual provider prerequisites;
there is no fallback to development credentials or trusting request headers.
Human or verified delegation modes require an independently validating native
handler and projector supplied in code. JSON cannot select a CLR type or create
that authority.

Public bootstrap settings name server certificate/private-key files and explicit
trust references. Private material is captured in sequential Startup, held until
native TLS and response work drains, and disposed by its real owner. Windows
temporary TLS key backing uses the existing bounded current-user custody owner;
no trust-store installation or machine key is performed.

Mappings contain exact issuer, subject, application and environment facts.
Grants use the existing complete-grant relationships for the four management
capabilities. Registration writes require the native workload's trusted self
mapping and a TargetOnly grant on the actual service identity. Application
reads, metadata edits and operation lookup use the stable
`management.application/<applicationId>` resource. An inaccessible application
returns 404. Lists filter before items and cursors, and expose no hidden totals.

Business traffic uses the configured exact IP and TLS port. A distinct loopback
operational port exposes only the existing health/metrics paths. Early native
listener isolation runs before those branches; operational paths are unavailable
on the business listener, and business paths are unavailable on the operational
listener. Host and forwarded headers cannot select the listener or profile.

## Finite request and state handling

The default per-replica admission is 32 requests, with immediate 503 capacity
refusal and a supported ceiling of 256. Ordinary/inventory request limits default
to 64 KiB/4 MiB; response limits default to 1 MiB/8 MiB. JSON depth defaults to 16.
The [portable contract](../../../../contracts/management/README.md) lists supported
wire ceilings and native field limits. Whole invalid inventories are rejected.

Pages default to 100 items and allow at most 500. Cursors are HMAC-protected and
bind the endpoint, actual caller/resource scope, ordering and page size to the
last returned item. They work across replicas sharing the same startup secret.
They describe a live view, not a snapshot. Responses admit complete items within
their byte budget; they do not eagerly load hundreds of maximum-size inventories.

The [persistence guide](Persistence/README.md) describes shared occupancy locks,
receipt replay, terminal registrations, independent report sequences, freshness,
cleanup and supported storage ceilings. Routine retention uses the existing
Heartbeat owner in bounded SQL batches; it does not elect a leader or release
job ownership when management reports become stale.

Focused native verification is recorded with the current change. Authored source
and local credential validation do not establish AKS federation, cert-manager
issuance, deployment or release qualification.

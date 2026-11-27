# Local container deployment

This explicit local recipe runs the independent management service and a new
PostgreSQL database. It uses an externally provisioned AMQPS broker and supplied
certificates. It does not create an identity provider, broker, Audit archive or
production certificate authority. Native integration tests retain their own
Testcontainers owners; these Compose resources are separate operator-owned data.

The image pins SDK 10.0.401 and ASP.NET/.NET 10.0.12 on Noble, using verified
linux/amd64 manifest children. Their primary registry indexes are
[SDK](https://mcr.microsoft.com/v2/dotnet/sdk/manifests/10.0.401-noble) and
[ASP.NET](https://mcr.microsoft.com/v2/dotnet/aspnet/manifests/10.0.12-noble).
The PostgreSQL digest is the same PostgreSQL 18 image selected by the existing
native fixture. Framework-dependent `linux-x64` folder publication preserves the
generated TypeManager inventory and its complete DLL closure. No trimming,
single-file or AOT profile is selected.

Run the standalone image build from the repository root:

```sh
docker build --platform=linux/amd64 -f implementations/csharp/applications/CIS.Management.Service/Dockerfile -t cislib-management:rm07-local .
```

Its adjacent `Dockerfile.dockerignore` includes only the source/build closure for
the service and the existing health probe. Build outputs, launch settings,
deployment inputs and private PEM/env files are excluded. The application-local
ignore does not change another image's context. The image runs as the base image's
nonroot application user and includes no settings, password or private key.

## Supply the private inputs

From this deployment directory, choose a fresh `CIS_MANAGEMENT_COMPOSE_PROJECT`
name for this new database and volume. Confirm that `172.28.7.0/24` is unused; if
selecting another subnet, update both the Compose address and the selected
settings' exact `Security.BusinessAddress`. Do not point this recipe at an
existing application database.

Create an untracked owner-restricted `private` directory and copy
`settings.certificate.example.json` to `private/settings.json`. The checked-in
JSON contains public trust/mapping references and explicit example grants; it is
not automatically loaded. Replace the broker hostname, TLS name, virtual host,
principals and owned namespace with your deliberate broker selection. The broker
must already support the existing durable Messaging topology. For a private
broker CA, set the existing public `Delivery.Broker.TrustedRootCertificatePem`
field to the PEM content, or override it through
`Delivery__Broker__TrustedRootCertificatePem`. That field is PEM content, not a
file path. Omission retains native system trust.

Place the selected server leaf/chain, separate private key, server root bundle,
client root bundle and exact client issuing-authority bundle at the PEM paths in
the JSON. Certificate mode requires native server/client validation. The example
deliberately selects the existing ShortLived policy: actual client and server
leaves must have lifetimes no greater than 24 hours and valid usages/chains. The
server SAN must match the address used by its consumer, such as `127.0.0.1` for
this loopback host mapping. Clients trust that server root explicitly. Client
identities come from valid exact URI SANs and receiver-owned mappings, never CNs,
body labels or forwarded headers. Do not install these roots in a trust store.

The example grants CountryData registration only. A separate application
automation identity receives the three explicit `AllOfType` administrative
resource grants. It is an application actor, not a human portal login. Narrow
those grants to `ExactResource` plus an actual `ResourceIdentifier` after the
application has registered if that is the intended operator scope. Human/native
delegation uses the explicit configured `entra-management` reference described in
the [operator recipe](../../../../../docs/modules/management/operator-security.md).
For that selection start from `settings.entra-management.example.json`, replace
every example tenant/object/client/resource value and retain workload mappings
and role-bound grants alongside the human mappings. Existing `Mode=entra`
continues to admit app-only workloads; neither example dynamically loads a
provider or obtains trust from headers.

The exact container address is intentionally a certificate profile, even for
local work. `Mode=local` remains loopback-only and cannot bind this private
container interface. There is no development fallback. The service publishes
business TLS only to host `127.0.0.1:7443`; operational port 7080 remains inside
container loopback.

Create these untracked private files; enter actual secrets only in them:

| File | Contents |
| --- | --- |
| `private/postgres-setup-password.txt` | Password for the new local PostgreSQL deployment role; one secret line. |
| `private/migration.env` | `CIS_MANAGEMENT_POSTGRES` with Host=postgres, Database=cis_management and the separate cis_management_setup deployment credential; `CIS_MANAGEMENT_CURSOR_KEY` as base64 for a 32–64 byte key. |
| `private/runtime.env` | `CIS_MANAGEMENT_POSTGRES` with the narrower cis_management login; the same stable `CIS_MANAGEMENT_CURSOR_KEY`; `CIS_MANAGEMENT_RABBIT_PASSWORD` for the selected publisher. |
| `private/topology.env` | `CIS_MANAGEMENT_RABBIT_SETUP_PASSWORD` for the distinct setup principal. |
| `private/pem/` | Only the server/private and public trust files named by the selected settings. |

The cursor key must remain the same across replicas/restarts. Do not generate a
new key per process. Keep environment files private, and make mounted PEM files
readable by the container's nonroot application UID (1654 for these images)
without making private keys world-readable. Runtime, migration and topology use
separate environment files; the running service never receives the setup broker
password. Optional `CIS_MANAGEMENT_*_ENV_FILE`,
`CIS_MANAGEMENT_POSTGRES_PASSWORD_FILE`, `CIS_MANAGEMENT_SETTINGS_FILE` and
`CIS_MANAGEMENT_PEM_DIRECTORY` variables in Compose can select other deliberate
paths. Relative paths resolve from this deployment directory. Do not commit or
include private inputs in an image build context.

## Apply the explicit deployment actions

These commands are run from this deployment directory after preparing its private
inputs and fresh project name:

```sh
docker compose -f compose.local.yml build management
docker compose -f compose.local.yml up -d postgres
docker compose -f compose.local.yml --profile deployment run --rm migrate
```

Migration installs the application, Operations and Messaging schemas in one
ordinary local transaction. It is separate from serving traffic and is not a
schema repair loop. The current executable also requires its cursor-key input in
migration mode, although no cursor or server private key is used by migration.

Create the runtime role explicitly in the new local database, using `psql`'s
interactive password prompt so the password is not a command argument:

```sh
docker compose -f compose.local.yml exec postgres psql -U cis_management_setup -d cis_management
```

```sql
CREATE ROLE cis_management LOGIN;
\password cis_management
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT CONNECT ON DATABASE cis_management TO cis_management;
GRANT USAGE ON SCHEMA cis_management, cis_operations, cis_messaging TO cis_management;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA cis_management, cis_operations, cis_messaging TO cis_management;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA cis_management, cis_operations, cis_messaging TO cis_management;
\quit
```

The runtime role must not own these schemas or have schema-changing privileges.
Use the selected runtime password in `private/runtime.env`. For another explicit
migration, review any new tables/sequences and extend the existing grants after
the deployment action.

Provision the selected broker topology with its setup principal, then start the
service using its separate publisher:

```sh
docker compose -f compose.local.yml --profile deployment run --rm --no-deps topology
docker compose -f compose.local.yml up -d management
```

Topology provisioning has its own two-minute cooperative deadline. It does not
load the database connection, cursor key or server private key. Existing broker
resource mismatches fail; it does not delete or replace resources. Ordinary
startup validates the installed graph and requires its provider prerequisites.
The example's runtime publisher needs only the existing provider's documented
rights on that owned graph, including narrow equivalence checks. It receives no
management administrative grant. `RequireLiveBrokerForReadiness=false` retains
the existing post-startup outbox behavior; it is not permission to skip the
selected native provider's startup prerequisites or claim an Audit destination
received a committed record.

## Observe and stop

The existing probe uses a current-user named pipe, not the operational HTTP
listener. The image enables `management-health` and runs the probe under the same
nonroot user:

```sh
docker compose -f compose.local.yml exec management dotnet /app/probe/CisHealthProbe.dll --pipe management-health --view readiness --timeout-ms 1000 --max-report-bytes 4194304
```

Exit 0 means the selected readiness view is healthy, 1 unhealthy, and 2 a transport
or protocol failure. If changing the pipe environment name, change the healthcheck
command with it. No curl package, anonymous business health route or new probe is
added. Same-user pipe communication uses the bounded writable `/tmp` tmpfs while
the application filesystem remains read-only.

The recipe bounds memory, processes, CPU and container log rotation; PostgreSQL
keeps fsync, full page writes and synchronous commit enabled. Application telemetry
and outbox have their existing finite retained-state limits. Those limits and
PostgreSQL WAL targets are not a filesystem disk quota; account for the owned
database volume and image/build cache separately. Keep the volume for a restart
when validating metadata/result recovery. `docker compose -f compose.local.yml down`
stops this selected project and retains its database volume. Removing that volume
is an explicit data-deletion choice, not service shutdown or a rollback proof.
The service receives a 120-second container stop grace period and PostgreSQL 60
seconds. The product still joins its real dependency/reporting drain. An eventual
infrastructure kill after that grace period does not establish completed cleanup
or remote rollback; recover uncertain commands with their original keys.

The recipe does not establish a real external Entra, AKS, cert-manager or broker
deployment proof. Native acceptance records identify the exact product host,
database, credentials and both-client calls that were actually executed.

# Central Management

RM-07 supplies a separately deployed management service and typed C# and
TypeScript clients. The service stores admitted application identities,
registration/presence and complete inventory snapshots, and implements one
administrative command: replace application display metadata. CountryData
remains the maintained tutorial application; this service is a product with its
own deployment, database and operational ownership.

Start with the [portable v1 contract](../../../contracts/management/v1/README.md)
and its checked-in [OpenAPI](../../../contracts/management/v1/openapi.json) and
[schema](../../../contracts/management/v1/contract.schema.json). They define the
thirteen installed APIs, finite wire limits, exact identities, decimal revision
and sequence strings, closed requests and compatible response additions.

- [Product service](../../../implementations/csharp/applications/CIS.Management.Service/README.md):
  actual `CISApplication` composition, trusted bootstrap, security/listeners,
  explicit SQL migration and broker topology provisioning.
- [Local/container deployment](../../../implementations/csharp/applications/CIS.Management.Service/deployment/README.md):
  pinned Linux build, explicit deployment actions, external secret/trust inputs
  and separate restricted operational probing.
- [Application persistence](../../../implementations/csharp/applications/CIS.Management.Service/Persistence/README.md):
  shared PostgreSQL arbitration, stable identities, receipt/sequence semantics,
  bounded inventory admission and telemetry retention.
- [Metadata command and recovery](../../../implementations/csharp/applications/CIS.Management.Service/Metadata/README.md):
  original-context Transactions/Operations/Audit integration, ETag concurrency,
  required Audit staging and original-key result recovery.
- [C# consumer guide](csharp-client.md) and
  [typed client](../../../implementations/csharp/src/CIS.Core/Management/Client/ManagementClient.cs):
  existing REST/security ownership, typed outcomes and bounded CommonTesting
  substitution.
- [Optional process reporting](registration.md): direct Startup registration,
  bounded Heartbeat reconnect, whole Jobs projection and actual dependency drain.
- [Managed admission](admission.md): mandatory selected bootstrap, finite Jobs,
  queue and effect HTTP boundaries, settlement and readiness independent of liveness.
- [TypeScript workspace](../../../implementations/typescript/README.md) and
  [management package](../../../implementations/typescript/packages/management-client/README.md):
  portable ES modules, per-call credentials and explicit server-only adapters.

Validated native credentials and trusted server mappings establish identity.
Current grants authorize the management service's actual application/environment
and exact `management.application/{applicationId}` resource. Reported instance
metadata, inventory, copied identifiers and discovery hints grant no authority.
Every read, page, mutation, replay and result lookup checks current permission.
Validated operator/delegation identity remains distinct from workload identity.

The product owns its data source, schemas and sole transaction completion.
Module constructors and registration remain passive, and existing Startup stays
sequential. Presence/inventory retention is disposable telemetry policy;
metadata operation duplicate protection belongs to the existing durable journal.
The original metadata effect, native decision and required outgoing Audit commit
in one original transaction. The existing Messaging runtime owns broker delivery;
Central Management supplies no destination Audit archive or new delivery queue.

Keep the HTTP exchange, business decision, original persistence and current
attempt cleanup separate. A prepared mutation needs known commit before success.
Replayed/found decisions retain their original committed fact despite a later
read owner's rollback or cleanup error. After uncertainty, retain the original
key and unchanged command; lookup NotFound is not rollback proof, and expiry
does not authorize reexecution. Native rejections retain their real error rather
than an invented metadata value. Mutations have no automatic resubmission.

CommonTesting scripts exercise bounded consumer behavior. The existing native
PostgreSQL/RabbitMQ and HTTPS suites supply their separate provider, atomicity,
authorization and recovery evidence. See the active change's verification record
for actual executed results; local tests do not establish deployment or release
qualification.

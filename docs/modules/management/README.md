# Central Management

The separately deployed Management service supplies typed C# and TypeScript
clients, admitted application identities, registration/presence, complete
inventory and guarded display metadata. RM-08 adds trusted configuration catalog
provisioning, immutable publication/releases, explicit assignment and adoption
over the same authority and durable store. CountryData remains the maintained
tutorial application; this service is a product with its own deployment,
database and operational ownership.

Start with the [portable v1 contract](../../../contracts/management/v1/README.md)
and its checked-in [OpenAPI](../../../contracts/management/v1/openapi.json) and
[schema](../../../contracts/management/v1/contract.schema.json). They define the
foundation and Configuration APIs plus optional companion reads, finite wire limits,
exact identities, decimal revision/sequence strings, closed requests and
compatible response additions.

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
- [Portal inspection](portal-inspection.md): readable application and report
  context, collapsed exact technical identifiers, unavailable logical feature
  catalog and separate Service API diagnostics.
- [Portal display metadata](portal-metadata.md): finite explicit review,
  strong preconditions and original-key recovery.
- [Portal Configuration authoring](portal-configuration.md): exact source drafts,
  native candidate preview, immutable publication and separate coherent releases.
- [Portal whole-map assignment](portal-configuration-assignment.md): explicit
  Canary, promotion and retained-release rollback without dropping unrelated pins.
- [Portal adoption](portal-configuration-adoption.md): separate desired,
  reported accepted and historical versions with bounded pages and independent freshness.
- [Local portal review data](portal-manual-review-data.md): isolated Country/Billing
  examples, explicit editor/reader permissions, native assigned JSON and truthful
  instance/job observations under a finite manual lease.
- [Configuration contract](configuration-contract.md): exact source-scoped
  discovery, versions, integrity, explicit absence and finite portable APIs.
- [Configuration editor context](configuration-editor-context.md): one exact
  binding, its actual Common/Application source pair and independently authorized
  declared root defaults; no effective-value or action authority.
- [Configuration service recipe](configuration-service.md): explicit trusted
  `Configuration.ManifestFile`/`QuotaBytes`, authenticated catalog apply, source
  discovery, immutable publication and original-key recovery. Discovery advertises
  only the selected, initialized handlers actually implemented by the backend.
- [Configuration distribution](configuration-distribution.md): immutable coherent
  releases, explicit whole-map promotion/rollback, scoped preview, original-key
  recovery and authoritative self snapshots/conditional reads.
- [Configuration retention](configuration-retention.md): shared logical quota,
  protected references and bounded release/publication history on the existing
  maintenance owner, independent of original result and Audit retention.
- [Configuration adoption](configuration-adoption.md): separately sequenced
  complete reporting, current-assignment convergence and independent freshness
  on the existing reporting and persistence owners.
- [Exact binding rollout reads](configuration-binding-reads.md): retained immutable
  release pages and adoption restricted before recipient expansion, with actual
  native resource authorization and bounded continuation.
- [Central root acceptance](../configuration/central-snapshots.md) and
  [C# configuration client/store](configuration-client.md): explicit local
  enrollment, strict whole-pair reads, provenance, synchronized retained-root
  acceptance and locally selected HotReload/RestartRequired behavior.
- [Same-session facade bootstrap](../application/core.md#preparation-and-readiness):
  native credential preparation, pre-constructor loading, final provider
  activation, live control and actual failed-Build/stop cleanup.
- [CountryData configuration recipe](countrydata-configuration.md): the existing
  CountrySettings opt-in, common/application publication, staged restart,
  rejection and controlled rollback; the default tutorial stays unmanaged.
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
- [Bounded portal discovery](discovery.md): literal application search, exact
  environments, authorized environment enumeration, metadata-only application
  definitions and current read-policy/ticket-bound cursors.
- [TypeScript configuration client](typescript.md): matching portable contracts,
  awaited native integrity, scoped conditional reads and actual transport results.
- [Optional Scheduler/catalog clients](scheduler-client.md): passive typed ports,
  strict bounded wire framing, exact preview context and original-key recovery.
- [Complete API discovery](api-discovery.md): compatible legacy operation arrays,
  complete installed pages and advisory application API eligibility.
- [Operator actions](operator-actions.md): exact source/binding eligibility,
  write and original-result recovery grants, and independent value disclosure.
- [Trusted cohort choices](configuration-cohorts.md): bounded authorized rollout
  groups from configured mappings, frozen enrollments and this binding's retained
  assignments, with no membership disclosure or new write authority.
- [Portal command references](portal-command-receipts.md): payload-free local
  original keys, bounded retention and joined lookup selection; authenticated
  context and command-screen integration remain separate work.
- [Protected portal state](portal-protected-state.md): real session associations,
  cross-view response isolation, memory-only drafts and dated outage snapshots.

Validated native credentials and trusted server mappings establish identity.
Current grants authorize the management service's actual application/environment
and exact `management.application/{applicationId}` resource. Reported instance
metadata, inventory, copied identifiers and discovery hints grant no authority.
Every read, page, mutation, replay and result lookup checks current permission.
Validated operator/delegation identity remains distinct from workload identity.

The product owns its data source, schemas and sole transaction completion.
Module constructors and registration remain passive, and existing Startup stays
sequential. Presence/inventory retention is disposable telemetry policy;
metadata and configuration operation duplicate protection belong to the existing
durable journal. Original effects, native decisions and required outgoing Audit
commit in one original transaction. Configuration retention protects current and
pinned references; deleting eligible content does not erase original decisions.
The existing Messaging runtime owns broker delivery; Central Management supplies
no destination Audit archive or new delivery queue.

An immutable publication identifies source bytes; a release pins the exact pair;
an assignment generation identifies a new controlled map decision. A root's
local revision starts at zero and advances only for accepted effective changes.
New central provenance can therefore be accepted without changing that revision.
Adoption compares those actual facts against the current target while keeping
freshness and historical success separate.

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

## Rule Engine counter capacity

Management projects Rule Engine counters after it accepts terminal execution
audit records. Each replica attempts one batch every five seconds. The batch
size is the smaller of 16 and the configured persistence cleanup batch size.
All rulesets in the same Management store share the pending-execution queue.

At the maximum batch size, the nominal ceiling is 3.2 terminal executions per
second per replica before database latency and contention. Sustained input
above the effective processing rate increases the backlog. Counter reads report
stale values while the scoped backlog remains. Counter projection runs
independently of rule evaluation and durable audit acceptance.

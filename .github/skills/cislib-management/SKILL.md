---
name: cislib-management
description: Implement, consume or review CISLIB Central Management APIs, portable clients, managed execution, Jobs inventory and central Configuration publication/distribution. Apply to CIS.Management and the independent management application; portal UI and external deployment qualification remain separate.
---

# CISLIB Central Management

Read [repository guidance](../../../AGENTS.md), the
[module guide](../../../docs/modules/management/README.md) and
[portable v1 contract](../../../contracts/management/v1/README.md).
Use the checked-in OpenAPI, canonical schema and exact-text conformance cases;
CLR or TypeScript reflection does not define the protocol.

- Keep the service an independent `CISApplication` consumer with its own data
  source and explicit SQL migration/broker provisioning. Core contains portable
  contracts, the existing REST-backed client and optional reporting, with no
  database/broker dependency. CountryData remains the tutorial. Central
  Configuration extends this product's existing authority/store and optional
  companion `IManagementConfigurationBackend`; it does not add compulsory members
  to `IManagementBackend`. Add no portal, scheduler, generic command engine,
  second configuration authority or Audit archive.
- Use the application's typed `IManagementBackend` boundary. PostgreSQL is the
  sole complete adapter: `Standalone` selects one API process, while `Shared`
  selects replicas on the same qualified durable primary, namespace, expected
  recovery epoch and cursor key. Validate the actual original native context;
  reject standby/multi-host fallback and incomplete providers. Explicit migration
  establishes stable authority and upgrades the schema; ordinary startup never
  repairs or replaces authority. Preserve recovery hold, independent expected
  epoch and cursor authority binding. Redis/Cosmos are future targets, not
  complete adapters. Read the
  [backend guide](../../../implementations/csharp/applications/CIS.Management.Service/Backends/README.md).
- Native credential validation and trusted mappings establish workload or
  independently validated human/delegation identity. Authorize the actual
  management service application/environment and resolved
  `management.application/<applicationId>` resource. Configuration additionally
  resolves actual catalog/source/binding/release resources; metadata discovery
  does not grant source/default value disclosure or mutation. Filter before
  pages/cursors; reauthorize mutations and recovery. Reported inventory, instance
  labels, expected cohort, permission hints and copied actor data confer no
  authority. Cohort membership comes from the native subject mapping captured
  in the admitted session.
- Preserve strict UTF-8/Unicode, closed command objects, compatible response
  additions, decimal Int64 strings and seven-fraction UTC timestamps. Keep native
  Jobs byte bounds and scoped identities, including observation mode. Admit whole
  bounded inventories and refuse a replacement rather than truncating it.
- Metadata replacement uses the original RM-02 owner and existing RM-03
  authorization/arbitration. Replay precedes the old-ETag handler. Conditional
  update, original decision and required RM-06 outgoing Audit share one original
  transaction and one completion. Replay repeats neither effect nor Audit; use
  existing Messaging delivery without a second journal or queue.
- Load the finite trusted catalog manifest and explicit quota/retention/pin
  policy independently of central content. Authenticated apply provisions stable
  resources; startup does no implicit DDL or apply. Source publications retain
  exact UTF-8 bytes. Releases pin an explicit common/application pair, including
  explicit absence, and creation uses the current assignment-map ETag without
  changing that map or activating content. Replace/Promote/Rollback are complete
  guarded map decisions with increasing generations. Preserve original-key
  arbitration before old-precondition/reference checks and required payload-free
  Audit. Read the [service recipe](../../../docs/modules/management/configuration-service.md)
  and [distribution guide](../../../docs/modules/management/configuration-distribution.md).
- Keep portable IDs/schema versions separate from persisted Configuration
  identity, immutable publication/release IDs, assignment generation and host-local
  accepted revision. Enrollment is explicit; update mode is locally selected,
  never remote authority. Central snapshots validate the independently trusted
  target, exact source scope, strict JSON and UTF-8 digest before the existing
  retained-root value gate. A failed read is not absence; 304 needs an exact
  retained pair and real authoritative cleanup, and grants no Management control.
  Use `IConfigurationReader` for consistent reads. Initial revision is zero;
  unchanged values can accept new provenance without a value event. RestartRequired
  loads initially, then stages effective changes until restart. See
  [central acceptance](../../../docs/modules/configuration/central-snapshots.md),
  the [C# adapter](../../../docs/modules/management/configuration-client.md) and
  [portable configuration contract](../../../docs/modules/management/configuration-contract.md).
- Keep native rejected errors distinct from typed values. Prepared requires
  known commit; Found/Replayed retain original commit despite current read
  rollback/cleanup. Preserve Unknown and the original caller key. NotFound or
  expired results do not prove rollback or authorize a new-key rerun. See the
  [metadata guide](../../../implementations/csharp/applications/CIS.Management.Service/Metadata/README.md).
- C# borrows the selected keyed native REST/security owner. TypeScript uses
  per-call credentials and an explicit server entry; browser imports acquire no
  Node/React/Next dependency. One entry deadline includes encoding, credentials,
  transport, decode, permitted read retries and actual cleanup. Writes do not
  retry automatically; cancellation signals and joins owned work.
- A passive `IManagementClient` does not select managed execution.
  `AddCisManagementRegistration` does: its gate starts closed, and sequential
  Startup must obtain live registration, direct authoritative control and
  sequential `IManagedStateReconciler` success before admitting work. Retained
  `RequireInitialRegistration` compatibility cannot make bootstrap optional.
  Control has no automatic retries, echoes the exact registration/attempt UUIDs
  and cannot silently rebind authority. Its maximum fifteen-second freshness
  starts at local monotonic request entry and includes cleanup/reconciliation.
  Every `IManagedWorkAdmission.TryAdmit()` rechecks freshness and prerequisites;
  snapshots/events are observations, never reusable permits. Failure, expiry or
  a clock gap closes admission; a fresh control check and reconciliation reopen
  it. `Stopping` is terminal.
- Enforce the same gate at actual new Jobs acquisition/occurrence/iteration,
  application Messaging delivery and explicitly selected effect HTTP endpoints
  using `RequireCisManagedWork()` after native authorization. In ContinuingOnly,
  preserve admitted callbacks, native lease rules, original transaction/result/
  Audit settlement, committed outbox publication and reply ingress. Keep permitted
  status/read/recovery paths authorized and reachable. Admission adds no callback
  revocation, business enablement or lease authority. `IHealthReadinessSource`
  contributes bounded retained facts without I/O; management loss changes
  readiness independently of process liveness. Read the
  [admission guide](../../../docs/modules/management/admission.md).
- Reporting uses a quiet dedicated REST alias, one existing Heartbeat owner,
  one latest observation and one pending complete inventory. Preserve uncertainty
  after an unconfirmed upload even when local content reverts to an earlier
  confirmed value. Jobs projection inspects all pages without resolving handlers.
  Keep callbacks safe at the native minimum interval; bounded failure/recovery
  diagnostics never suppress requests, health facts, cancellation or faults.
- Selected facade bootstrap prepares actual PEM/Entra credentials and direct
  registration/control before Build, loads initial roots before constructors,
  retires actual bootstrap work, then adopts the same session into one final
  provider. Preserve the exact native policy/binding/clock and final provider's
  meter ownership. Fresh post-Build control and Configuration reconciliation
  precede sequential business Startup; old control cannot reopen work. Real gaps
  coalesce on the existing Configuration owner, with one recovery kick rather
  than a sweep on every short control callback. Read
  [facade ownership](../../../docs/modules/application/core.md#preparation-and-readiness).
- Adoption adds one complete independently sequenced status capture to that same
  reporter and stop barrier, retaining one uncertain original capture. The native
  owner and frozen supported session authorize reports. Keep current desired
  convergence separate from historical acceptance, registration/adoption
  freshness, offline and no-instance observations; incompatible old host sessions
  remain visible as unknown. Unknown future modes/states never become success.
  Reported labels do not prove distinct processes or typed fleet compatibility.
  See [adoption](../../../docs/modules/management/configuration-adoption.md).
- Retention uses the existing bounded cleanup owner and one shared row allowance,
  with exact logical content/metadata/adoption quota deltas on its original
  context. Protect assigned/pinned releases and every retained release's source
  references, including shared publications. The applied N retains newest eligible
  unreferenced releases per binding and orphan publications per source. Pinned-full
  capacity refuses new effects. Content deletion cannot erase original result
  recovery or required Audit. Read
  [retention](../../../docs/modules/management/configuration-retention.md).
- Signal reporting cancellation early while preserving admitted Jobs/Audit
  barriers. Close/drain REST before awaiting reporter/Heartbeat settlement;
  capture admitted factory ownership on failed Build. A remote close is best
  effort, never a teardown prerequisite. Startup's coordinator stays independent.
  Read the [reporting guide](../../../docs/modules/management/registration.md).

For the maintained consumer, use the
[CountryData opt-in/operator recipe](../../../docs/modules/management/countrydata-configuration.md)
and existing example unit project. Only its existing CountrySettings is enrolled
as RestartRequired. Keep bootstrap endpoints/trust/credentials independent,
polling explicit and the default application unmanaged. Use the
[TypeScript guide](../../../docs/modules/management/typescript.md) for portable
integrity/conditional-read semantics; C# and TypeScript preserve their actual
native result differences rather than inventing a common success response.

Reuse bounded public CommonTesting for consumer facts and the existing native
PostgreSQL/RabbitMQ/HTTPS/package fixtures for their distinct boundaries. Use
ordinary MSTest and the TypeScript workspace's pinned compiler/native Node
commands. Source, scripts and local identity proofs do not establish external
Entra federation, cert-manager issuance, destination Audit persistence or release
qualification. Keep actual execution and remaining limits in the current change.

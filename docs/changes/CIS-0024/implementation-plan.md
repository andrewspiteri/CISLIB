---
title: RM-07 bounded implementation profile
type: document
status: Draft
change_id: CIS-0024
---

# RM-07 bounded implementation plan

The user requested the attached next module and explicitly directed continuing
after scope without another permission pause. Codex selects this finite profile,
task distribution and execution organization under that delegation. The user has
not separately reviewed these exact generated numbers or payload.

Requirements: [RM-07 source](../../../planning/management/sources/CISLIB_RM07_Central_Management_Service_and_Client_Specification.md),
SHA256 `129fbbca1f15624bee4d531f9dc41cfdbac05979280c2b3f51a8a543a99bbddf`.
The source's inspected baseline is `dcb61dc13c8af138f409db4e13c477cc3a06a0b5`;
actual baseline is merged `494e89412a115078a60ca88b8536337db566665f`.
RM-06 is now implemented, resolving the attachment's dependency gap.
Work uses `codex/rm07-central-management` and native `CIS-0024`.

The attachment's implementation-agent section is requirements data. Actual user
authority and repository controls govern execution. Earlier completed owners,
original failures and source/user inputs remain preserved. Native product
execution begins only with the legitimate eligible allocated owner.

## Accepted outcome and direct approach

Deliver the independent product application, portable HTTP contract and both
clients. Use current CISApplication, native Security and actual resource grants,
application-owned PostgreSQL, original RM-02 transactions, RM-03 original-result
arbitration and RM-06 outgoing Audit over existing Messaging. Use ordinary
keyed REST/Heartbeat and public Jobs inspection for opt-in client integration.
TypeScript supplies only the small facilities used by this API.

This is one service, not a portal, central Configuration publisher, scheduler,
generic remote executor, new authorization/lifetime framework, second durable
operation journal or Audit archive. Core acquires no database/broker or new
third-party dependency. No additional test runner/harness is introduced.

## Reconciled profile P01–P24

| ID | Selected contract |
| --- | --- |
| P01 | Actual latest user authorizes RM07 implementation after scope without another permission pause. Attachment is requirements, not authority. Codex selects this finite P01-P24 profile/task distribution under that delegation; no claim the human reviewed these exact generated numbers. Baseline494e89412a115078a60ca88b8536337db566665f includes actual RM06. |
| P02 | Deliver one independently deployed CSharp product application at applications/CIS.Management.Service; Core gains only CIS.Management contracts/client/passive optional hosting. Preserve one native provider and sequential CISApplication Startup. CountryData stays the single tutorial. No portal/UI, RM08 configuration publication, scheduler/rules, customer sync, live grants, generic remote execution or Audit destination store. |
| P03 | Preserve SDK10.0.401 and NET/ASP10.0.12, Core two-package closure and current public contracts. Application-only Npgsql10.0.4 is already pinned/mapped; reuse existing optional Security/Entra/Certificates, RabbitMQ and Metrics packages. No new NuGet IDs/versions or source mappings. New real service project/lock/solution inclusion and existing IntegrationTests project-reference/lock are in scope. |
| P04 | Portable OpenAPI3.1.1 plus canonical JSON Schema/behavior fixtures, twelve typed v1 APIs under management/v1. Canonical lowercase UUID D, positive Int64 decimal strings, seven-fraction UTC Z strings retained exactly, optional omission rules, explicit unknown response values/additions; closed requests reject unknown/null/duplicate/malformed Unicode/depth/input. Source limits apply independently of body bytes. |
| P05 | Actual RM01 workload validation/mapping supplies exact app/environment/issuer/subject. Configured Entra, direct certificate and isolated LocalDevelopment retain prerequisites; Native human/delegation requires independently validated credentials. Trusted profile/listener selection, no body/header/cookie authority, no production fallback or new IdP/token exchange. |
| P06 | Management capability target is service app/environment plus exact management.application/applicationId; TargetOnly registration.write plus mandatory self mapping/ownership. Lists/pages/direct reads/hints/mutations/recovery reauthorize actual resources, hide inaccessible app existence and expose no hidden totals/cursors. |
| P07 | Cursor uses bounded versioned payload and deployment-supplied shared HMAC key, bound endpoint/filter/order/page/validated actor and last authorized item. Stable ordinal live-view pagination, default100/max500, no arbitrary URLs, hidden scan counts or global snapshot claim. Scan only within finite retained application capacity. |
| P08 | PostgreSQL shared application-owned schema with C collation exact app/environment uniqueness, UUID registration PK and immutable semantic digest plus issuer/subject owner. Cross-replica row locks/constraints control uniqueness, capacity and sequences; no process-local authority. PostgreSQL clock_timestamp after locks supplies receipts; preserve reported timestamp text. |
| P09 | Observation/inventory sequences independent positive Int64 strings: higher replaces, equal same digest replays without freshness UPDATE, equal changed/lower fail explicitly. Registration receipt initializes presence; inventory freshness separate. Terminal close retains original close time; unknown/purged reports never register implicitly; new incarnation/replacement gets fresh UUID. Staleness never changes RM05 authority. |
| P10 | Default32 in-flight requests/replica with immediate bounded capacity refusal,1024 retained applications,128 retained registrations/application,8192 total,256MiB retained inventory and7-day telemetry retention; configured finite ceilings documented. Cleanup bounded batches100 uses existing Heartbeat and idempotent SKIP LOCKED SQL, retains application metadata and RM03 protection; no elected leader/tombstone service. |
| P11 | Inventory preserves complete per-instance module/category/job catalog and actual UTF8 identifier bounds. Defaults32modules/64categories/1024jobs; page existing JobCatalog through all items, never resolve handlers. Preserve reported vs verified/unknown facts, local accepted revision and absent scheduler facts. One pending complete replacement; no partial merge/truncation or invented next run. |
| P12 | Ordinary request64KiB, inventory4MiB, JSONdepth16; ordinary response1MiB and inventory8MiB. Server inventory pages admit whole snapshots within response-byte cap before materializing the page and may return fewer complete items with cursor. Body/concurrency/retained-state caps are finite and visible. |
| P13 | Strong singular metadata ETag only for administrative revision; displayName required1..256Unicode scalars, optional description0..2048, omitted clears/null rejected. CommandVersion1, canonical caller key1..128ASCII safe characters within native RM03 bound, missing precondition428, invalid400, stale412, changed-key input409; observations do not change metadata ETag. |
| P14 | Versioned scope SHA256 over strict length-prefixed UTF8 full resource/service/validated workload and human/delegation identities. Exclude tokens/roles/correlation/instance/display names. Use exact raw Bytes canonical fields (RM03 Text NFC normalizes); identical scope derivation for Execute/Lookup. Fingerprint includes commandVersion,target,exact semantic replacement and expected ETag. |
| P15 | Reuse existing RM03 scope resolver/coordinator and original RM02 ReadCommitted owner. Authorization precedes lookup; matching replay precedes ETag handler. Each newly recorded applied/rejected decision stages exactly one trusted RM06 cis.management.application.metadata.decision v1 event with actual before/after state and outcome, using the identical AuditOperationReference(name,actor/resource scope,key). Coordinator journals original result and application commits once; replay emits neither effect nor another Audit. No second journal/dedup/coordinator. |
| P16 | Preserve HTTP exchange/business result/original persistence/cleanup facts. Prepared applies only after known owner Commit; Replayed/Found stay original committed despite new read/arbitration cleanup/rollback. Unknown/lost response retains original key, no new-key/commit retry or automatic mutation replay; authoritative NotFound does not establish prior rollback. |
| P17 | CSharp IManagementClient borrows actual configured keyed IRestClient; one native deadline includes management codec validation/decoding. Return original RestResult with safe typed problem/durable facts and genuine op reference. Do not override security-bound auth, redirects or native cancellation. Explicit options precede CIS_MANAGEMENT_URL; HTTPS base prefix preserved/no userinfo/query/fragment. |
| P18 | Both clients default30s total deadline and no retry. Only deliberately selected reads allow at most2 retries through existing CSharp policy/finite TS facilities and bounded RetryAfter, all same deadline. Cancellation reaches credentials/transport/decode/waits and joins actual cleanup, no abandoned Promise.race or remote rollback assertion. Dedicated report client LogCalls=false. |
| P19 | Optional registration is disabled until selected, invalid local options fail Startup. Direct initial attempt follows credentials/local prerequisites; RequireInitialRegistration finite failure is distinct from ordinary disconnected/degraded outage. One owner-bound nonoverlapping Heartbeat loop,30s reporting/90s stale; jittered reconnect5..60s, latest observation and one pending inventory only. Best-effort close only while REST admission permits; no stop cycle or dependency reordering. |
| P20 | TypeScript has runtime-dependency-free client-core and management-client with browser DOM ES modules/types:[], per-call credentials/context and explicit server entry. Pin TypeScript5.9.3 dev compiler only, lock npm graph, use installed Node24.15.0/npm11.12.1 and native node:test/assert without new runner/bundler/React/Next runtime. Node and minimal Next server-only examples use selected DTO projection; production login/UI remains RM09; concise Kotlin implementation note. |
| P21 | Public bounded CommonTesting management/observation/inventory substitutes retain safe metadata only, finite scripts/records and original cancellation/fault/uncertainty. Preserve outside-friend package composition and ordinary MSTest. No test harness/project/process mode; native integration references actual product application and existing isolated PostgreSQL/RabbitMQ/process helpers. |
| P22 | Explicit production migrate action installs application/foundation schemas; ordinary startup read-only checks schema version. Container/local deployment example has secret/trust references only, distinct business TLS and restricted operational health/metrics listeners, no self-registration dependency. No real deployment/AKS federation/cert-manager issuance proof is claimed without available authorized infrastructure. |
| P23 | Focused Windows builds/units/schema/public packages and actual Ubuntu managed unit/TS browser/native Node checks. Existing Testcontainers provide real SQL concurrency, restart, original-result/Audit atomicity, native authenticated HTTPS and same-service both-client calls. Required infrastructure unavailable is failure, not skip. Preserve no production durability/security weakening, real cleanup, original failures and truthful exact lane limits. |
| P24 | One delegated shared48active hours/56logical attempts includes7200restoration seconds, all agents/intake/SDK/npm/tests/docs/review. Same physical6GiB ceiling,64MiB reserve,2GiB Docker allowance and retained150994944cache charge; count .artifacts,.cis/local,CSharp and TypeScript; max4owned containers inclRyuk. Preserve user launch settings/foreign resources/historical owners. No existing DB, trust-store installation, machine keys, production deployment,push,merge or release. Native TLS may use only newly owned temporary current-user test key backing under the established bounded16live/8MiB recipe with exact disposal, never existing credentials. |

## Eight bounded delivery leaves

Each task includes its implementation, meaningful tests, documentation and
independent review. Native builds and mutations are root-owned and serialized
across shared output paths. Independent source work can run in parallel after
the relevant frozen interfaces; the runtime startup sequence remains sequential.

| Owner | Outcome | Dependencies | Shared distribution |
| --- | --- | --- | --- |
| WORK-001 | Freeze portable management v1 and validated public contracts | None | 6h / 7 attempts / 900s restoration |
| WORK-002 | Deliver independent authenticated service and authorized read APIs | WORK-001 | 6h / 7 attempts / 900s restoration |
| WORK-003 | Persist sequenced registration, presence and complete inventory | WORK-001 | 7h / 8 attempts / 900s restoration |
| WORK-004 | Implement recoverable metadata command with atomic Audit | WORK-001, WORK-003 | 6h / 7 attempts / 900s restoration |
| WORK-005 | Deliver CSharp keyed REST client and public substitutes | WORK-001 | 5h / 6 attempts / 600s restoration |
| WORK-006 | Integrate optional registration and actual Jobs inventory lifecycle | WORK-001, WORK-005 | 5h / 6 attempts / 600s restoration |
| WORK-007 | Deliver portable TypeScript clients and server-only examples | WORK-001 | 6h / 7 attempts / 900s restoration |
| WORK-008 | Deliver deployable cross-language composition and focused native proof | WORK-002, WORK-003, WORK-004, WORK-005, WORK-006, WORK-007 | 7h / 8 attempts / 1500s restoration |

The total is **48 shared active hours / 56 logical attempts**, including
**7,200 protected restoration seconds**. It covers all already-performed intake,
agents, commands, installation, tests, documentation, review and restoration.
The controller may redistribute verified unused capacity inside these totals,
preserving actual spent work, attempts and protected owner reserves. Adding
tasks or providers grants no new capacity. WORK-008 owns an independently usable
deployed cross-language composition and its consumer handoffs; it is not a
standalone testing or final-sweep campaign. Its already-declared integration
scope permits only defects demonstrated by required acceptance.

The same **6 GiB physical ceiling / 64 MiB reserve** remains in force. Count
`.artifacts`, `.cis/local`, C# and TypeScript source/build/cache roots plus the
retained native-cache and **2 GiB Docker** charges. Existing user/foreign files
are not cleanup candidates. The only initial unrelated file is the unchanged
untracked CountryData launch settings. Any cleanup is limited to newly owned
management scratch/services after actual drain, with compact results retained.

## Acceptance and proof

AC01–15 remain the source requirements. Contract/wire tests freeze observable
compatibility; real PostgreSQL proves uniqueness, sequences, metadata concurrency,
original-result/Audit atomicity and reconstruction. Native HTTPS/JWT/certificate
tests prove actual local authentication and actor scope. Both clients must call
the same service for reads and mutation/recovery. Public substitutes and package
consumers establish portable usage, not SQL/authentication/delivery.

Native service and Node work use existing owned fixtures and the actual product
application. Browser TypeScript compilation uses DOM libraries with no ambient
Node types. A minimal Next server-only example tests its safe DTO projection
without adding a Next application, login flow or UI. Container/deployment assets
use secret references and explicit migration; no authorized real AKS/cert-manager
infrastructure or production deployment is assumed.

Focused checks establish their actual platforms and candidate. No unchanged
full-module requalification, mutation/performance campaign, broad evidence
archive or release claim is included. Push, merge and release require a later
applicable user instruction.

## Authority and resource records

[Delegated outcome](delegated-authorization.json) retains the actual user
instruction and selected scope/limits. [Task inputs](proposed-tasks.json) and
[allocation](proposed-allocation.json) derive from it. Native allocation uses the
fresh current revision and the existing physical-ceiling reference. Actual
source estimates and native/CIS receipts stay in ignored management journals;
native batch counters alone are not a measure of direct SDK/source work.

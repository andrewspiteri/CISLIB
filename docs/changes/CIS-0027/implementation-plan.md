# RM-08 bounded implementation proposal

Status: **Implementation authorized on 24 November 2026.** After reviewing this
proposal, Andrew Spiteri instructed: "ok let's move ahead with the implementation."
CIS now records that current decision in `authorization.json`; the shared limits
below remain unchanged and include prior intake. Native owner/allocation admission
is recorded separately before product work. Earlier RM-07 delegation was not used
as RM-08 authority. Push, merge, release and external deployment remain excluded.

Requirements: [preserved RM-08 v1.0](../../../planning/configuration/publication/sources/CISLIB_RM08_Central_Configuration_Publication_and_Distribution_Specification_v1.0.md),
SHA256 `592f6af2e49d982fa7bfbe4581fac8f66eeb9939afd7c4c6fa9008e3e1e2db2f`.
Actual baseline: `abfbcafaa6ef2f3c3c18304244e4dafc1dd5c852` on
`codex/rm08-central-configuration`. See [current-code reconciliation](reconciliation.md).

This remains the reviewed profile. The eleven definitions in `proposed-tasks.json`
have now been loaded through CIS, preserving the original seed001 identity.
The earlier dictionary-key error came from supplying a bare task object instead
of the supported `tasks` envelope; corrected inputs applied successfully after
authorization. The original failures remain retained. Read canonical task state
and current allocation for actual eligibility; this narrative grants no capacity.

## Proposed outcome and boundaries

An authorized operator can publish immutable source content, create a coherent
release, deliberately assign/promote/roll back default and trusted cohort maps,
and inspect current adoption. Explicitly enrolled C# consumers load authenticated
configuration before construction, preserve retained roots and existing merge/
reader/event semantics, and safely hot-apply or stage locally declared restart
updates. Management authority remains RM-07's separate responsibility.

Use the existing service, qualified PostgreSQL authority, original Transactions/
Operations/Audit graph, REST, Configuration, Heartbeat, Security, Metrics and
public/native fixtures. No new dependency, pin, source mapping, database provider,
framework, portal, secret resolver, timer/broker, generic CRUD/command engine,
second provider, automatic rollout/restart or persistent client cache is proposed.
CountryData stays the single tutorial, database-free and unmanaged by default.

## Proposed profile P01–P28

| ID | Concrete selection |
| --- | --- |
| P01 | The user's 24 November instruction authorizes this reviewed implementation profile/shared allocation. Fresh CIS-0027 owners carry the work after native admission. Completed owners remain closed; preserve all prior intake and original decisions. |
| P02 | Preserve the current single Core assembly, optional adapter boundaries, MSTest/IDE workflow, existing pins/dependencies and canonical OpenAPI/schema. No new package IDs or tooling installation. |
| P03 | Explicit one-to-one local enrollment binds case-sensitive portable ID, exact positive schema string, fixed Configuration host scope, versionless persisted definition, trusted management application binding and local update mode. No remote CLR names or automatic discovery enrollment. Deterministic defaults belong to the declared schema contract; incompatible shape/default/validation changes require a new schema. |
| P04 | Add an optional paired-snapshot capability and immutable candidate provenance beside existing public contracts. Require it for central entries; explicitly route noncentral entries through their current store semantics. Legacy custom stores/pipelines remain usable without compulsory new members. |
| P05 | Exact decoded payloadJson UTF-8, lowercase hex64 SHA-256, strict Unicode/no BOM/duplicate decoded names/depth and source validation before overlay. Server structural declaration is finite data shape, not a JSON Schema/rule engine or execution of consumer constructors/validators; final typed/cross-property validation stays local. |
| P06 | One dedicated configuration REST operation alias plus request fallback per configuration client. Keep wire operation IDs, durable names and resource/actions distinct. Two current Management profiles consume 28 pairs; two config profiles consume four, preserving 32. CountryData's single Management/config/countries composition consumes 18. Runtime polling/report alias is quiet; administrator logging remains separately selected. |
| P07 | Configuration-only 1-MiB command/resolved-response profile; ordinary Management requests stay 64 KiB. Source decoded JSON 256 KiB, depth16, 256 enrolled definitions, 16 cohort overrides, 16-MiB aggregate accepted/candidate/in-flight source ownership per process, one current desired candidate per definition. Tighter callers remain effective. |
| P08 | Central-only logical materialization ceilings: depth16, at most 65,536 value nodes per root, 4,096 elements per collection, 256 declared members per object and 64-KiB scalar UTF-8 text. Validate defaults/composition and reserve owned buffers before expansion. These are logical limits, not a promise to cap arbitrary host/validator memory. |
| P09 | Same selected PostgreSQL backend and original participant graph; upgrade from current schema v2 to the next compatible version preserves authority, metadata and old operation/outbox scopes. Unsupported/incapable/uninitialized providers never advertise durable configuration publication. Redis/Cosmos and in-memory publication are excluded. |
| P10 | Migration seeds an empty stable configuration-root catalog/ETag only. Load one operator-controlled local manifest; fixed authenticated catalog status/apply endpoints bind its digest, current ETag, original key and reason. Native operator authorization, original result and required payload-free Audit share the actual transaction. Normal replicas validate applied state; they do not silently apply changed manifests. Allocate existing app/env resources from trusted bindings before consumer bootstrap. |
| P11 | Manifest provisions immutable schema declarations, allowed application/source bindings, local host-scope expectations and trusted issuer/subject cohort mappings. Same schema identity cannot be silently redefined. Cohort is frozen in the admitted registration; new bootstrap is required for membership change. Labels/UUIDs are not credentials or extra physical-process proof. |
| P12 | Immutable publications and application-specific releases with exact common/application references or explicit absence. No floating latest or absent-on-error. Lock/check references atomically with creation/GC; validate the full resolved encoded envelope plus worst supported target/session overhead before assignment. |
| P13 | One complete assignment map, strong ETag and positive monotonic generation; initialized unassigned map has no generation. Whole-map Replace/Promote/Rollback intent is explicit, including override removals/retentions and bounded reason. Identical higher decisions still advance generation; exhaustion refuses, never wraps. |
| P14 | Four fixed original-key operation families: manifest reconciliation, source publication, release creation and assignment mutation. Fingerprint exact content/references, resolved scope/schema, complete intent, original precondition and reason. Matching replay precedes old-precondition evaluation, reauthorizes and repeats neither effect nor Audit. Known commit is required for newly committed success; Unknown/NotFound never authorizes a new-key rerun. |
| P15 | Read source/catalog metadata independently from content authority. Preview is side-effect-free, scoped and redacted without value-read permission; show changes/overrides/targets without claiming deployed typed acceptance. Audit/status may contain authorized bounded IDs/codes/reasons, never configuration payloads or native exception text. Metrics use only fixed declared dimensions and mandatory application/environment identity; never instance/registration/release IDs, unbounded configuration names, payloads or exception/reason text. |
| P16 | Thirteen specified operations plus two fixed catalog status/apply operations and four typed recovery operations: 19 new operations, 32 total with existing RM-07, below current64-feature discovery bound. Exact schemaVersion query, cursor binding, models and operation names freeze in WORK-001. No public business endpoints or product UI. |
| P17 | Separate substitutable configuration client/result/problem branch preserves IManagementClient and metadata DTOs. Native C# REST codecs enforce operation-specific bounds. TS adds POST, Promise-capable decoding awaited inside the existing deadline/cleanup owner using native WebCrypto, explicit non-mutating validate/preview, and opt-in conditional304. No global redirect relaxation, POST retries or integrity checks after return. |
| P18 | Authoritative resolved reads bind application/env/schema/registration/cohort/authority to one joined original SQL observation. A304 requires an exact retained snapshot for that session; backend failure/hold cannot assert unchanged. Track highest observed generation, reject equal conflicts, recheck session/cancellation/supersession immediately before commit. Snapshots never renew RM-07 authority. |
| P19 | Bootstrap uses explicit existing standalone HttpRestClient/security implementations before Build, with no retry and a bounded total120-second preparation budget (validated positive up to300 seconds; ordinary per-call limits still apply). No I/O in constructors/resolution, unauthenticated fallback or temporary provider. Endpoint/trust/credentials/store connection remain independent. |
| P20 | Add a narrow supported native credential preparation/adoption seam, including optional CertificatePemRuntime's actual renewal target. Preserve policy/binding identity and one cleanup barrier per resource. At final Build bind normal DI clients using the real final IMeterFactory, join/retire bootstrap transports, and hand off the same authenticated registration/cohort/session. Never register an unmetered transport as a hosted borrowed one. |
| P21 | Roots are populated before consumers. Credential adoption, Configuration activation/gap reconciliation, first Management control/reconciliation and remaining Startup run sequentially. Consume the initial same-session config epoch before initial StartAsync's short reconciler, which treats false as startup failure. Slow Build requires fresh control before opening. Failed preparation/Build/cancellation and Stop join all actual owners. |
| P22 | Required config reconciliation epochs arise only from initial/session/real connectivity gaps, not every five-second control attempt or the reconciler's own incomplete closure. Trigger/coalesce the existing serialized Configuration owner; short control callbacks use retained completion/readiness instead of sweeping256 definitions. Require an authoritative reconciled outcome before reopening, without another timer/task backlog. |
| P23 | Accepted values/checksum/revision/provenance commit under the same root gate, including valid unchanged acceptance. HotReload retains root and advances local revision/events only for effective runtime changes. RestartRequired stages only at runtime; new process fetches current live state. Returning to active values clears pending restart without an event. Subscriber faults are separate from acceptance. |
| P24 | Initial bootstrap requires every enrolled root accepted. For a running process, an authoritatively identified Rejected or PendingRestart outcome with intact accepted roots can complete config reconciliation; ordinary failed unidentified polling cannot invent a rejection or clear data. Faulted/Disabled makes last-success provenance historical and readiness false. No default age limit or desired==applied admission policy; base health criticality stays explicit. |
| P25 | Polling is required using the host's explicit positive interval;30 seconds is only an example. Adapter declares None; optional hints remain coalesced reread requests. Source age never clears accepted data. Same existing owner/lifecycle, quiet normal operation and persistent monotonic failure/recovery budgets safe at10ms. |
| P26 | A separately sequenced complete configuration-status stream shares the existing reporting owner. Higher replaces; equal identical retains receipt time; equal conflicting/lower refuses. One pending/uncertain capture, separate registration/adoption freshness, current-map projection, unknown/offline/no-recipient states explicit and no payload/checksum/process-proof claims. |
| P27 | Require an explicit deployment-selected total logical configuration byte quota and retention policy; choose32 MiB only for the isolated fixture. Count current configuration metadata/adoption as well as content. Retain latest10 eligible unreferenced releases, protect assigned/pinned releases and referenced sources, reject pinned-full capacity. Pin policy is guarded catalog metadata. Cleanup uses the existing bounded owner/batches and is independent from RM-03 results and RM-07 telemetry. |
| P28 | CountryData opt-in enrolls existing CountrySettings as RestartRequired; document common+override, staged assignment, rejection, restart and rollback. No new root/business behavior just for a demo. Existing native/public fixtures prove HotReload, coherent readers, two replicas, actual actor/transaction/cleanup and package/portable behavior. No persistent cache, image-build or qualification campaign. |

## Coherent implementation stories

A narrow WORK-012 supports006's confirmed-connectivity recovery. CIS preserves
started task contracts, so the tiny existing Configuration owner retry seam has
its own accepted Configuration scope and 45 minutes/one attempt redistributed
inside the same76-hour/80-attempt outcome.006 receives one additional hour from
unused completed-story distribution; no prior spent cost is reset. Total
restoration remains8,100 seconds and physical storage remains unchanged. The
eleven consumer outcomes and their acceptance obligations remain the same.

Implementation organization checkpoint: WORK-009 may begin its independent
server projection and isolated reporter files after002/003/004/007, alongside
WORK-006. Its composed same-session reporting/cleanup validation still joins
WORK-006 before completion; WORK-011 also depends on both. This removes a start
dependency without removing an acceptance obligation, adding a task or changing
the shared allocation. Codex holds the shared main/factory edits with006 and
the new reporting partial/server files with009.

For WORK-010, the deployment-selected retention count also keeps the newest N
eligible unreferenced publications per source. Assigned/pinned releases and all
references from retained releases are excluded before eligibility ranking. This
avoids immediate collection during publish-to-release preparation while bounding
otherwise orphaned content. The selected default remains ten; no new timer,
endpoint or package is introduced. Immutable schema/source/catalog identities
and original Operations/Audit records remain independently retained.

Every story owns its code, focused tests, documentation, independent review and
completion evidence. The final consumer story is a real maintained application
handoff and combined operator workflow, not a separate generic verification sweep.
The IDs below now exist as canonical CIS owners. Read their current status and
`work-allocation.json` before execution; a table entry alone grants no admission.

Each medium story has one bounded consumer or authority outcome across existing
interfaces. The main complexity is original-transaction/reference concurrency
(002/003/010), retained-root acceptance (004), actual native-resource ownership
(005/006), portable transport compatibility (001/007/008), and separately
sequenced reporting (009). Story011 demonstrates the combined operator workflow
in the maintained consumer. Keep those boundaries rather than splitting out
test-only, documentation-only or generic final-validation tasks.

| Proposed ID | Owned outcome and checks | Hours | Attempts | Restoration seconds | Dependencies |
| --- | --- | ---: | ---: | ---: | --- |
| WORK-001 | Freeze portable/enrollment/session contracts, exact19 APIs, shared strict C# wire codec and independent vectors; compatibility/data/resource design and intake accounting | 8 | 8 | 600 | — |
| WORK-002 | Trusted catalog initialization, native actor/capabilities, schema migration and immutable source publication; original-key/Audit/authorization/provider tests | 8 | 8 | 900 | 001 |
| WORK-003 | Coherent releases, scoped preview, whole-map assignments/cohorts/promote/rollback and conditional authoritative reads; race/replay/two-replica tests | 8 | 8 | 900 | 001,002 |
| WORK-004 | Optional paired-store/enrollment and actual value-gate acceptance/provenance, hot/restart modes and faults; legacy/read/event/concurrency/late-candidate tests | 8 | 8 | 600 | 001 |
| WORK-005 | Native security/credential bootstrap preparation and certificate renewal/transport adoption; actual trust, rotation, failure and drain tests | 6 | 6 | 900 | 001 |
| WORK-006 | Same-session pre-Build facade bootstrap and sequential host activation; real constructor, failed-Build, cancellation, stale-control, outage/reconnect tests | 6 | 6 | 900 | 004,005,007 |
| WORK-007 | C# configuration client, conditional central adapter, routed legacy stores and bounded public helpers; deadline/digest/recovery/32-alias/public-consumer tests | 6 | 6 | 600 | 001,004 |
| WORK-008 | Portable TS parity, POST/effect classification, awaited integrity and scoped304; schema/vector/browser/native-body/cleanup compatibility tests | 6 | 8 | 600 | 001 |
| WORK-009 | Separately sequenced adoption capture/report/projection on existing owners; actual self authority, replay/freshness, current-target, unknown/no-instance tests | 6 | 6 | 600 | 002,003,004,006,007 |
| WORK-010 | Quota accounting and reference-safe history/pins/cleanup on original store owner; encoded-capacity, GC races, pinned-full and pruned-result recovery tests | 6 | 6 | 600 | 002,003 |
| WORK-011 | CountryData opt-in recipe and combined operator/public/native handoffs; actual constructor/session/adoption/restart/rollback/two-replica workflow and concise AC closure | 8 | 10 | 900 | 003,006,008,009,010 |
| **Shared total** | **All agents, already-performed intake, source, tests, docs, review and restoration** | **76** | **80** | **8,100** | |

These are hard shared maxima, not separate per-agent grants. Reserve
8,100 seconds and 64 MiB for restoration. Delegate task organization and ordinary
redistribution within the same totals; preserve consumed work and every protected
reserve. The current applicable scope/profile/allocation decision and fresh
canonical owners are recorded through CIS. No completed prior owner is reopened.

Implementation admission has now applied the user's decision. The controller's
current distribution moves four hours from011 to001 to retain the prior intake
and contract integration:001 has12 hours and011 has4 hours. All other time,
attempts and protected restoration reserves remain unchanged, as do the shared
76-hour/80-attempt maxima. The reviewed table above preserves the initial proposal;
the canonical allocation input records the actual distribution and revision.

## Source parallelism and sequential runtime

Freeze ports/schema/resource IDs and operation budgets in001. That story owns
the shared C# wire types/codec used by server and client;007 owns their REST
transport integration. Backend native tests therefore do not wait for007.
Then source002,
004,005 and008 can proceed on separate paths; root schedules at most three
subagents and serializes shared outputs/native builds/CIS writes.003 follows002's
durable schema;007 follows the local contracts;006 composes actual credential,
Configuration and C# client work.009 and010 own distinct reporting/retention files.
Shared registration, migration and hosting files have one writer at a time.

Runtime order remains independent bootstrap identity/trust -> Type Manager and
bindings -> same-session live registration/control with gate closed -> paired
Configuration preparation -> one provider/consumer construction -> credential
handoff and Configuration activation/gap reconciliation -> fresh Management
control/reconciliation -> remaining Startup/readiness/work admission. Source
parallelism never makes that sequence concurrent.

## Physical and execution limits

Keep the existing physical six-GiB counted ceiling. Count `.artifacts`,
`.cis/local`, C#/TS trees, existing144-MiB external native cache, conservative
eight-MiB owned TLS backing and the two-GiB Docker allowance inside it. W01 remains
limited to at most16 newly owned current-user imports, actual drain/deletion and
no trust-store/machine-key/existing-credential access. Native PEM fixtures need
no certificate-store installation.

The final intake observation counts 6,143,951,737 bytes, leaving 298,499,207 bytes
of headroom after the protected reserve and existing allowances. Propose at most
32 MiB net source/build growth and 224 MiB new retained native scratch/artifacts
(256 MiB combined, leaving about 30 MB of margin at this observation),
with per-phase expected-growth admission checks and joined removal of only this
change's newly owned regenerable scratch. Retain concise failures, inputs/hashes
and native receipts. No deletion of historical fixtures, global pruning, new
SDK/image/package download campaign or ceiling increase is included. If real
admission cannot fit, stop ordinary work before the protected reserve and present
the exact resource difference; do not silently run beyond it.

Reuse existing fresh isolated PostgreSQL/RabbitMQ Testcontainers only, at most
four live task containers including Ryuk, existing image pins and per-case
isolation. No existing database/credentials, external deployment, image rebuild,
push, merge, package publication or release is included. Validate Windows locally
and the applicable actual Linux/public consumer boundary using retained tools
or the existing CI path when separately publishing; do not claim a CI/image pass
from source review. No new installer or broad coverage/mutation campaign.

Preserve the unrelated CountryData launch settings, SHA256
`ea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784`.

## Acceptance ownership and evidence

| Criteria | Primary proposed owners | Required observation |
| --- | --- | --- |
| AC01,05,10,12,13,14,15,16 | 004,007,011 | Existing legacy/default/custom-store/root/reader/event contracts; direct actual acceptance, unchanged, rejection, pending restart and terminal fault |
| AC02,03,08,11,17,18 | 005,006,007,011 | Authenticated pre-construction values and same session, failed preparation/Build/drain, independent authority expiry, conditional poll recovery and late/superseded fences |
| AC04,06,07,09,19,20,21 | 002,003,010,011 | Real original-context PostgreSQL effect/result/Audit, exact references and scope, map races/rollback, two replicas/restart/hold and recovered original keys |
| AC22 | 009,011 | Actual self-report sequencing, independent receipt freshness and current-target projection without invented convergence/process proof |
| AC23 | 001,007,008 | Both languages exact large decimal revisions/local zero/timestamps/payload Unicode/digest, unknowns and meaningful public consumer compatibility |
| AC24 | 001,002,005,007,008,010,011 | Encoded/retained/shape/alias/diagnostic limits, reference races/pinned capacity, truthful capabilities/idempotent manifests and actual owned cleanup |

Use ordinary affected MSTest, existing schema/conformance tests, native Node and
browser compilation, real provider/native HTTPS, package/source consumers and
appropriate API compatibility checks. Root coordinates pinned formatting on
explicit changed C# files and verifies it. Scripted clients prove local state and
ownership; they do not prove SQL atomicity or deployment behavior. Record one
small representative volume-sensitive end-to-end result and its declared
bytes/counts/shape/concurrency/time, not a separate benchmark campaign. No manual
test catalogue is added without a separate applicable obligation.

Required concise implementation checkpoints: durable publication/map recovery;
constructor-visible central values with same-session host handoff; then actual
operator/adoption/restart/rollback consumer workflow. At this planning checkpoint,
all RM-08 acceptance observations remain pending.

## Implementation handoff

The preceding final sentence records the planning checkpoint. Current actual
observations and practical limits are in [the acceptance matrix](acceptance.md)
and [verification](verification.md). The same authorized totals cover all agents,
source, native execution, metadata, review and restoration. Task011's consumer
join now has five hours, transferred from task002's unused allocation; task002
retains 6.25 hours. No shared total or existing restoration reserve changed.
The narrowly scoped confirmed-recovery support task012 is separately recorded
in the canonical plan and allocation; it owns no new worker or timer.

After the842f60e8 branch review, three narrowly scoped corrective tasks013–015
use seven hours/six attempts transferred from unused003/004/008 capacity. The
[review-correction record](review-corrections.md) describes the three fixes,
their tests/documentation and applied allocation. Shared totals, all prior
consumption and every earlier restoration reserve remain unchanged. Completed
implementation tasks001–012 stay complete; none was reset or reopened.

# RM-07 v1.1 correction

The current user request authorizes reviewing the updated specification and making the necessary changes. The earlier RM-07 instruction delegates execution without another permission pause. Codex selected the finite profile below; its exact numbers were not separately human-reviewed. CIS-0024 remains complete, and this correction uses fresh owners on the existing unmerged branch.

## Requirements and gaps

The preserved [v1.1 source](../../../planning/management/sources/CISLIB_RM07_Central_Management_Service_and_Client_Specification_v1_1.md) has SHA256 `5e3700b5df9dd95362e4774dbf565842bef3bc8f216a219f9b4caf14d366a2e2`. The original v1.0 source and implementation history remain intact. Attachment requirements are product inputs; execution authority comes from the user's requests.

| Updated requirement | Existing gap | Correction owner |
| --- | --- | --- |
| Authoritative control API, attempt echo, maximum fifteen-second grant | Twelve APIs, reporting-only clients | WORK-001 / WORK-002 |
| Mandatory live managed bootstrap | Optional initial registration and no admission gate | WORK-003 |
| Request-start freshness, first failure closes, reconcile before reopen | Reporting connectivity is observational | WORK-003 |
| ContinuingOnly preserves admitted finite effects | No distinction between new work and settlement | WORK-004 / WORK-005 |
| Jobs acquisition, scheduled occurrence and finite iteration boundaries | Existing local capacity and lease admission only | WORK-004 |
| Messaging application admission and explicit effect HTTP admission | No managed admission at dispatch | WORK-005 |
| Readiness independent from liveness | Critical health affects both | WORK-005 |
| Typed actual backend and capability profile | Routes depend directly on PostgreSQL store | WORK-002 |
| Stable namespace authority, primary qualification, explicit recovery hold | Startup schema validation alone | WORK-002 |
| Standalone and Shared modes, effective inventory limit | One PostgreSQL deployment recipe | WORK-002 / WORK-006 |
| Updated AC14–20 and retained AC01–13 | Earlier evidence proves v1.0 only | Each implementation owner / WORK-006 |

## Bounded execution

One shared allocation: 48 active hours, 56 logical attempts, including 7,200 restoration seconds. Agents, intake, source, tests, documentation and review consume these totals. The existing physical six-GiB ceiling and 64-MiB reserve remain unchanged. Two GiB of Docker retention, 144 MiB retained external native cache and eight MiB owned TLS backing count inside it. No image rebuild campaign is included; actual Docker retention must fit its allowance. Native fixtures own at most four containers including Ryuk, use fresh isolated databases and join cleanup.

| Task | Hours | Attempts | Restoration seconds | Dependency |
| --- | ---: | ---: | ---: | --- |
| WORK-001 Control/client/admission contracts, tests and docs | 6 | 6 | 900 | — |
| WORK-002 PostgreSQL backend/authority/recovery/modes, tests and docs | 8 | 10 | 1,500 | shared port freeze |
| WORK-003 Managed lifecycle/freshness/reconciliation, tests and docs | 10 | 10 | 1,500 | shared port freeze |
| WORK-004 Jobs admission/wakeup, tests and docs | 8 | 10 | 1,200 | shared port freeze |
| WORK-005 Messaging/HTTP/readiness, tests and docs | 8 | 10 | 1,200 | shared port freeze |
| WORK-006 Integrated focused evidence and provider handoffs | 6 | 6 | 900 | matching source freeze, including 007 |
| WORK-007 Existing Health reader, focused tests and current docs | 2 | 4 | 0 | frozen readiness-only semantics |

WORK-001 froze the small shared ports on disk before parallel source began. The native task updates remove completion dependencies for WORK-002–005 at that freeze; integrated validation waits for matching implementations. Once all authors froze the matching candidate, unstarted WORK-006's completion dependencies were replaced by that source-freeze checkpoint so it can own cross-module formatting/build/verification before individual task closure. WORK-005 also uses the existing managed scope for facade composition. Backend, lifecycle and worker source run in parallel on distinct files. Startup itself stays sequential. Root serializes CIS mutations, builds, native service tests and shared output use. Each task includes its tests, documentation and review. No completed owner is reopened.

Readiness-only prerequisites require a narrow existing Health reader amendment: Readytrue still requires healthy/started/notstopping; Readyfalse may additionally reflect an explicit local prerequisite. The delegated scope reconciliation names the existing Health contract/docs paths and preserves default wire output and Healthcheck's established HTTP 500 NotReady response. No new report version is introduced.

CIS preserves the executable scope and history of started owners. The reader,
its existing protocol mutation tests and current Health contract documentation
therefore form coherent supporting WORK-007. Two ordinary hours and four attempts
move from unstarted WORK-006; every existing task's protected restoration capacity
and the shared 7,200-second restoration reserve remain intact. WORK-007 owns no
external resource fixture. Intermediate metadata refusals and actual elapsed
costs remain in the ignored intake journal; no permission was inferred from a
previous exact payload and no resource total increased.

## Selected profile

- Managed execution is opt-in; the passive client has no bootstrap or background owner. Selected managed execution is closed from construction and always requires live registration, authoritative control and required local preparation.
- States are AwaitingBootstrap, Ready, ContinuingOnly, Reconciling and Stopping. Admission is checked at each actual new finite-work boundary. There is no revocation token for already admitted work.
- Control runs every five seconds on the existing nonoverlapping reporting Heartbeat owner, with at most fifteen seconds confirmed freshness measured from monotonic request start through actual cleanup. Every attempt has a fresh canonical UUID nonce and explicitly disables retries. First failed/refused/expired exchange closes admission.
- Control replies bind canonical authority, registration and attempt IDs; the opaque revision is bounded but not numerically compared. Old generation/registration/authority responses cannot reopen. Reconnection is insufficient: sequential module-owned reconciliation succeeds before reopening. Local prerequisite loss and conservative suspension detection require revalidation.
- PostgreSQL is the sole complete provider. Standalone uses a dedicated primary; Shared uses multiple API replicas over one durable qualified primary and namespace. Future Redis/Cosmos profiles remain unsupported until a compatible atomic Operations/Messaging provider exists.
- Explicit migration initializes stable namespace authority/control/recovery state. Ordinary startup validates it. Recovery hold or unexpected authority/epoch blocks bootstrap/control/mutation. Snapshot rollback requires trusted operator reconciliation; minting another UUID cannot restore lost original-result protection.
- Keep original metadata operation scopes and one RM-02/RM-03/RM-06 transaction. Bind new cursor format to namespace/authority. Service-info reports the effective inventory maximum.
- New Jobs/queue/effect HTTP admission closes during management loss. Admitted callbacks, still-valid lease renewal/fencing, native RPC replies, original-result lookup and existing outbox/Audit/transaction settlement retain their existing owners.
- Readiness includes local managed/backend prerequisites without converting a management outage into liveness failure. No additional health server, scheduler, DI/configuration framework, database abstraction framework or test harness.
- No new dependencies, pins, source mappings, existing database access, external infrastructure, trust-store installation, push, merge or release. Preserve the unrelated CountryData launch settings file byte-for-byte.

## Validation and evidence

Use focused ordinary MSTest/CommonTesting, independent wire vectors and the pinned TypeScript compiler/Node tests. Reuse existing isolated PostgreSQL/RabbitMQ/native HTTPS and public package fixtures. Qualify backend isolation, unchanged original-result recovery and actual managed-host startup/outage separately from scripted lifecycle facts. Run affected formatting on explicit changed files and verify it. Document platform/deployment proof limits honestly; earlier v1.0 passes are retained evidence, not a new v1.1 pass.

After the initial outage setup exposed the existing 32-operation REST bound, the
controller transferred two unused WORK-001 logical attempts to WORK-006. The
shared total remains 56; protected restoration and time/storage limits are
unchanged. The corrected test uses two typed workload profiles and the existing
native administrative HTTP fixture.

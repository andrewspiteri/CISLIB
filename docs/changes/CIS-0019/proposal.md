---
title: "Messaging and reliable delivery (RM-04 Parts A and B)"
type: change-proposal
record_format: proportionate
status: Proposed
change_id: CIS-0019
outcome: "Deliver transactional event and command publication, broker-backed duplicate-safe consumption and durable retry/terminal responsibility, then typed request/reply and RM-03 result recovery on the same messaging machinery. Keep RabbitMQ optional, Core provider-free, operations explicit and all resources bounded."
baseline_kind: git
baseline: "669d286711340e7f63398256cee27575932f58f4"
graph_build_id: "sha256:bf7b91eedf53d5acb325e7a28a86adbe0cbd4db60e8b092665eeb97c0a5a3cd5"
impact_roots: "[]"
repository_baselines_path: creation-inputs.json
repository_baselines_sha256: "sha256:0c4e3754b502eb6785781324b03441bd6a45366d877d82d91c6b5423635b9ac7"
authority: derived-outcome-authorization
---

# CIS-0019: Messaging and reliable delivery (RM-04 Parts A and B)

## Outcome

Deliver transactional event and command publication, broker-backed duplicate-safe consumption and durable retry/terminal responsibility, then typed request/reply and RM-03 result recovery on the same messaging machinery. Keep RabbitMQ optional, Core provider-free, operations explicit and all resources bounded.

## Impact roots

No roots declared. Supply roots to `cis impact analyse`.

## Constraints

- Necessary tasks and supporting work derive from the recorded outcome authorization.
- Changes outside its scope or overall limits require a specific decision.

## Acceptance criteria

- Deliver the authorized outcome and its task-specific acceptance criteria within the shared constraints and limits.

<!-- cis:outcome-view -->

## Authorized outcome and limits

Derived from [the canonical authorization](authorization.json), decision `CISLIB-RM04-2026-11-22:explicit-proceed-both-parts:P01-P20`, recorded for Andrew Spiteri. Tasks reference this shared context; their organization is not a separate human review.

Implement RM04 Messaging Parts A and B under the preserved specification and P01-P20: transactional reliable events/commands with production PostgreSQL inbox/outbox and optional trusted RabbitMQ, bounded retry/terminal responsibility, then typed request/reply and original RM03 result recovery on the same machinery, with tests/docs/CommonTesting/skill and real-provider evidence. No release expansion.

- `composition`: Existing hosting/facade stop order, bounds/readiness, Logging/Metrics/exporter and Part A real-provider/package acceptance. Paths: `.artifacts/messaging`, `.cis/local/messaging-intake`, `.github/skills/cislib-metrics`, `contracts/messaging`, `contracts/metrics`, `docs/catalog.yml`, `docs/changes/CIS-0019`, `docs/modules/messaging`, `docs/modules/metrics`, `implementations/csharp/src/CIS.Core/Application`, `implementations/csharp/src/CIS.Core/Messaging`, `implementations/csharp/src/CIS.Metrics.OpenTelemetry`, `implementations/csharp/tests/CIS.Core.IntegrationTests/CommonTesting`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Messaging`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Metrics`, `implementations/csharp/tests/CIS.Core.IntegrationTests/RestClient`, `implementations/csharp/tests/CIS.Core.UnitTests/Messaging`, `planning/messaging`.
- `contracts`: Bounded owned wire contracts, immutable definitions/options and narrow store/transport/handler/result ports. Paths: `.artifacts/messaging`, `.cis/local/messaging-intake`, `conformance/messaging`, `contracts/messaging`, `docs/catalog.yml`, `docs/changes/CIS-0019`, `docs/modules/messaging`, `implementations/csharp/src/CIS.Core/Messaging`, `implementations/csharp/tests/CIS.Core.UnitTests/Messaging`, `planning/messaging`.
- `dispatch`: Independent native security/DI scope, inbox arbitration, handler and one-shot Operations orchestration, committed acknowledgement. Paths: `.artifacts/messaging`, `.cis/local/messaging-intake`, `contracts/messaging`, `docs/catalog.yml`, `docs/changes/CIS-0019`, `docs/modules/messaging`, `implementations/csharp/src/CIS.Core/Messaging`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Messaging`, `implementations/csharp/tests/CIS.Core.UnitTests/Messaging`, `planning/messaging`.
- `dispositions`: Durable targeted retries/terminal/rejection provenance and bounded authorized inspection/redrive/retention. Paths: `.artifacts/messaging`, `.cis/local/messaging-intake`, `contracts/messaging`, `docs/catalog.yml`, `docs/changes/CIS-0019`, `docs/modules/messaging`, `implementations/csharp/src/CIS.Core/Messaging`, `implementations/csharp/src/CIS.Messaging.RabbitMQ`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Messaging`, `implementations/csharp/tests/CIS.Core.UnitTests/Messaging`, `planning/messaging`.
- `postgres-store`: Production explicit PostgreSQL installation, original-context outbox/inbox/disposition/administrative storage and real atomicity. Paths: `.artifacts/messaging`, `.cis/local/messaging-intake`, `contracts/messaging`, `docs/catalog.yml`, `docs/changes/CIS-0019`, `docs/modules/messaging`, `implementations/csharp/src/CIS.Core/CIS.Core.csproj`, `implementations/csharp/src/CIS.Core/Messaging/PostgreSql`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Messaging`, `implementations/csharp/tests/CIS.Core.TestSupport/Messaging`, `implementations/csharp/tests/CIS.Core.UnitTests/Messaging`, `planning/messaging`.
- `public-consumer`: Public bounded CommonTesting doubles, actual integrated package consumer, module guide/example/skill/routes and Part B integrated review. Paths: `.artifacts/messaging`, `.cis/local/messaging-intake`, `.github/skills/cislib-messaging`, `.github/skills/cislib-operations`, `.github/skills/cislib-transactions`, `AGENTS.md`, `README.md`, `contracts/messaging`, `contracts/operations`, `contracts/transactions`, `docs/catalog.yml`, `docs/changes/CIS-0019`, `docs/modules/common-testing.md`, `docs/modules/messaging`, `implementations/csharp/src/CIS.CommonTesting`, `implementations/csharp/tests/CIS.Core.IntegrationTests/CommonTesting`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Messaging`, `implementations/csharp/tests/CIS.Core.UnitTests/CommonTesting`, `planning/messaging`.
- `publication`: Committed-only reservation/publication/settlement and explicit/convenience staging on original contexts. Paths: `.artifacts/messaging`, `.cis/local/messaging-intake`, `contracts/messaging`, `docs/catalog.yml`, `docs/changes/CIS-0019`, `docs/modules/messaging`, `implementations/csharp/src/CIS.Core/Messaging`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Messaging`, `implementations/csharp/tests/CIS.Core.UnitTests/Messaging`, `planning/messaging`.
- `rabbit-provider`: Optional official RabbitMQ transport, native evidence projector, explicit topology and bounded newly owned broker fixture. Paths: `.artifacts/messaging`, `.cis/local/messaging-intake`, `contracts/messaging`, `docs/architecture/csharp-dependency-policy.md`, `docs/catalog.yml`, `docs/changes/CIS-0019`, `docs/modules/messaging`, `implementations/csharp/CisLib.slnx`, `implementations/csharp/Directory.Packages.props`, `implementations/csharp/NuGet.config`, `implementations/csharp/src/CIS.Messaging.RabbitMQ`, `implementations/csharp/tests/CIS.Core.IntegrationTests`, `implementations/csharp/tests/CIS.Core.TestSupport`, `implementations/csharp/tests/CIS.Core.UnitTests/Messaging`, `planning/messaging`.
- `rpc`: Typed requests/disposable tickets/local waiting/response matching and fresh current-attempt Operations result recovery on Part A. Paths: `.artifacts/messaging`, `.cis/local/messaging-intake`, `contracts/messaging`, `docs/catalog.yml`, `docs/changes/CIS-0019`, `docs/modules/messaging`, `implementations/csharp/src/CIS.Core/Messaging`, `implementations/csharp/src/CIS.Messaging.RabbitMQ`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Messaging`, `implementations/csharp/tests/CIS.Core.UnitTests/Messaging`, `planning/messaging`.

- Atomic Terminal+terminaloutbox beforeack; forged/conflicting/malformed delivery cannotpoison canonicalinbox. Verified bounded native at-least-once DLX for inputthatcannotbe stored; otherwise leaveunacked and stopconsumer visibly. Quorumreject-publish, no native defaultdelivery-limit/TTL bypass or second retryscheduler.
- CountryData remains sole tutorial/databasefree default; focused existingfixturecomposition satisfies example alternative. Preserve untracked launchsettingsSHA256ea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784. Push,merge,release,externaldeployment/broadqualification campaigns excluded.
- Default indefinite history/protection; explicit capability-gated max256inspection/maintenance/redrive without payloaddefault; preserve pending/blocked/retry/provenance/replydependencies. Audited redrivegeneration preserves identity; finitehorizon relationships validated, noOperations cleanup.
- Eachcoherent medium task includes meaningful tests/docs/scopedpinnedformat+verify/independentreview. Serialize sharedbuilds/providerlanes; parallel only frozen disjoint interfaces/files. Real SQL/broker A01-A12 then B01-B06, actual exporter/package perpart, bounded representativeworkload. Single-node evidence notquorumfailover; absent LinuxSDKpackage/deployment/fullrelease proof stays unverified.
- Existing host/startup sequential; narrow facade stoporder hook drainsMessagingbeforeMetrics/Heartbeat/data sources. NativeRabbit recovery soleowner. Sevenconditionalstreams435finitecombinations at16aliases, standardbuckets/centralidentity/globalcapacitiesunchanged, no rawIDs/scopes/endpoints/payloads labels; quietnormal/boundedfailure diagnostics.
- Fresh isolated Docker fixtures only: pin official image digests before pull, new owner labels/volumes/generated credentials/TLS materials and loopback listeners. PostgreSQL18 with normal durability max512MiB/2CPU/128PIDs/16connections/pool6; RabbitMQ4.4.1 max512MiB/2CPU/256PIDs and finite queuebytes/logs. No existing database/broker/operatorcredential/truststore/privatekey access. Delete only verified newly owned resources after actualdrain, no engineprune.
- Immutable version1 strict UTF8JSON envelope/bytes/digest, stable portable names and UUIDs; receipt/staged/outboxcommitted/brokerconfirmed/processed are separate. Metadata/requestattempt/replyroute never changes RM03businessfingerprint. Incoming metadata establishes no authority.
- Inbox arbitration before effects, actualscoped handler outsidecallback, atomicbusiness/result/inbox/outbox/reply then knowncommitbeforeack. Postrollback expectedordinal reacquire preserves concurrentwinner, unique targeted retrychain;5scheduledordinals with1s/5s/30s/2mdelays+jitter, crashphysicalinvocations separately visible.
- One module in ordered Parts A/B; shared envelope/outbox/dispatcher/security/retry and Operations machinery. No new generic bus/DI/Configuration/security/telemetry framework, workflow/saga/scheduler/portal/proxy, alternate protocol/broker, persisted Task, second idempotency coordinator, sample application or custom test harness.
- One shared230400active seconds72logical attempts including7200restorationseconds covers all agents, alreadyperformed intake, code, tests, docs, review, formatting and restoration. Controller may redistribute without multiplying capacity or resetting costs. Retain measured native costs and conservative unmeasured source estimates distinctly from automatic counters.
- Oneopaque requesterendpoint/runtime;normalacknowledgedreplyqueue,10munusedexpiry/maxreplyage>=maxwait+margin. Expectedauthenticatedresponder+exactattempt/originalmessage/type/route beforecompletion; unauthorized/unmatchedreplycannotconsume legitimatewait. Freshattempt recoversoriginalauthorizedRM03result undercurrentreplyroute;expiry/conflict/incompatibilityneverreexecute.
- Original RM02 context; sequential native callbacks; handler/broker waits outside them. Only owner completes/disposes. One RM03 Execute per original context; Prepared is uncommitted, Replayed still needs new inbox/reply commit, Unknown remains uncertain and commit is never automatically retried.
- Passive immutable bounded registration, max16aliases eachregistry.256KiBpayload default<=1MiB,16KiBmetadata,32headers/8KiB,32reservedmetadata/4parallelpublishes,8consumerconcurrency/prefetch16 and processwork32/sharedownedencodedbytes32MiB. Reserve capacity before copying/materializing, including retained earlyreplies.
- PostgreSQL independent versioned schema/explicit installer, RC-only parameterized BCLADO and DBunique/conditional state. Reservecommit then publishoutsideSQL then fresh fencedsettlement. Lostconfirms/uncertain settlement preserves sameidentity/bytes; transientoutage cappedbackoff never silentlyexpires durablecommands.
- Preserve existing physical CISLIB-CIS-0001-STORAGE-2026-11-10 ceiling6442450944bytes and67108864reserve over .artifacts/.cis/local/implementations/csharp plus8388608existingnativecharge. A2147483648Docker allowance is inside that same ceiling for one newly owned PostgreSQL and one RabbitMQ fixture. Check measured admission, count all new caches/scratch and preserve historical protected/accounting material.
- RPCwait30sdefault/5mmax entry-based monotonic deadline;<=256waiters; reserve/registerbeforestage, disposableticket retains earlyreply and releasesonecapacity. Independent committedreceipt confirmation beforewait, no callerownercommit. Separate submission/exchange/protocol/businessfacts; timeout/cancellation neverassertremote rollback.
- RabbitMQ.Client7.2.2 only in optional CIS.Messaging.RabbitMQ. Permit exact RabbitMQ.Client/System.IO.Pipelines/System.Threading.RateLimiting source IDs and inspect actual net10 locks/pruning. Preserve Core Microsoft closure, all existing dependency pins and SDK10.0.401/NET+ASP10.0.12. Application owns native database provider. Necessary optional transitive lock resolution stays in this scope; no source wildcard or new source.
- TLS/hostname/chainvalidated broker+vhost, actualvalidatedUserId, restrictedprincipals/nofederationorimpersonator. Private native ticket/projector through existing SecurityHostContextScope; no serialized caller authority or fake HTTPSOutboundpolicy. Retry uses current service authority and exactretained record/ordinal/digest/trustedscope, never originalcaller impersonation.
- The current user explicitly directs completing both attached RM04 parts without requesting further authorization. Codex selects this finite profile/allocation and organizes tasks under that delegation; this record does not claim separate human review of these exact numeric choices. Preserve source SHA25623aa65f5145730c83564e3ed3cc30f1210bb75058ec4d9d2ceab1476c650d952.

The total is 230,400 active seconds and 72 logical attempts, including 7,200 protected restoration seconds. Storage is 6,442,450,944 bytes including existing files and 67,108,864 protected reserve bytes.
Owned roots: `.artifacts` (count only), `.cis/local`, `implementations/csharp` (count only).
Controllers: Codex. Task organization: delegated; resource distribution within the total: delegated.
Visual UI: not applicable; manual-test catalogue: no obligation recorded.
<!-- /cis:outcome-view -->

---
title: "RM-04 reconciliation"
type: document
status: Draft
---

# Accepted request and baseline

The user requested RM-04 intake, contract reconciliation and a bounded plan, then
explicitly directed: "the module is impemented in 2 parts, keep going until it is
complete without request me for authorization." This delegates ordinary profile,
task, dependency and finite allocation choices within the attached module. It is
not represented as a separate human review of the generated numeric payload.
Preserve this instruction alongside the exact source; no historical approval is
reused or edited. Push, merge, release and external deployment remain separate.

Local and connected remote main both resolve to the source's reviewed baseline
`669d286711340e7f63398256cee27575932f58f4`. Work uses
`codex/messaging-reliable-delivery`. The existing untracked CountryData launch
settings retain SHA256
`ea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784`.

## Current contracts

| Existing owner | Reconciliation |
| --- | --- |
| RM-01 | Establish an independent native SecurityHostContextScope using a private broker-evidence ticket and exact projector. Serialized metadata and stored callers never authenticate. Broker profiles use Native/Inbound; REST's HTTPS Outbound contract remains unchanged. |
| RM-02 | Pass the original explicit context to all same-database work. Native callbacks are sequential; orchestration/handler/broker waits stay outside them. Only the owner completes and disposes. Preserve Committed, NotCommitted and Unknown. |
| RM-03 | Invoke ExecuteAsync once per original context with explicit business normalization. Prepared is uncommitted. Replayed still requires a new inbox/reply transaction to commit. Preserve exact identity, Succeed/Reject factories, conflict/expiry/incompatibility and authorized fresh recovery. |
| Hosting | Registration is passive. The existing host performs schema/topology checks and starts resources. Add a narrow facade stop-order hook so Messaging drains before Metrics/Heartbeat and host-owned data sources; no runtime dependency on the facade or new lifecycle framework. |
| Metrics | Seven conditional messaging streams, central identity, finite aliases and existing buckets/global limits. No IDs, scopes, endpoint instances, payloads or raw broker labels. Backlog observations are retained bounded values, never scrape-time SQL. |
| CommonTesting | Extend the optional public package and existing actual package consumer. Scripts remain bounded and establish no SQL/broker/authentication guarantees. |

RM-03's outgoing SQL is test-only. Implement new production inbox/outbox metadata
with its own version and explicit installer; do not promote fixture SQL to API.
The portable module remains in CIS.Core. RabbitMQ.Client belongs only in the new
optional CIS.Messaging.RabbitMQ package. The application owns DbDataSource/Npgsql.

## Selected profile

| Decision | Concrete contract |
| --- | --- |
| P01 — Delivery | One feature, ordered Parts A/B. Events fan out to configured subscriptions; commands target one logical service. No global/per-ledger ordering guarantee. |
| P02 — Envelope | Version 1 strict UTF-8 JSON, stable portable names/positive schema versions, UUIDs, bounded owned payload/metadata and SHA256 immutable transport digest. Reject duplicate keys/malformed UTF-8/unknown required versions before a handler. No CLR names or arbitrary discriminators. |
| P03 — Identities | Logical MessageId survives retries/redrive. Inbox key is captured receiving application/environment/subscription/MessageId. Transport attempt and reply fields never enter RM-03 business fingerprints. |
| P04 — Registration | Immutable startup snapshot; at most 16 aliases in each route/subscription/request/store registry. Explicit codecs/capabilities and exact database/contract consistency. No construction-time network/schema/worker I/O. |
| P05 — PostgreSQL | BCL ADO.NET, PostgreSQL 18, original RC context, parameterized commands and independent versioned messaging schema. Atomic uniqueness, separate conflict-read snapshot, database-time fencing. |
| P06 — Staging | Encode/validate/own bytes before success; any staging fault poisons the active context. Receipt means staged; convenience submission reports actual completion and stable identifiers. |
| P07 — Publication | Metadata-only batch up to 32, acquire bytes before materialization, at most 4 native publications. Reserve/commit, publish outside SQL, then fresh fenced settlement. Confirmed requires confirms and route checks. Unknown leaves recoverable identical bytes. |
| P08 — Broker | Optional official AMQP 0-9-1 RabbitMQ.Client 7.2.2 with RabbitMQ 4.4.1. Pin registry digests before pull and inspect actual locked dependency graph. Core and existing pins stay unchanged. |
| P09 — Topology | Durable quorum queues/exchanges, persistent mandatory publication, manual single ack, bounded reject-publish queues. No default native delivery-limit loss, business TTL or second delayed-retry scheduler. Opt-in explicit provisioning validates rather than replaces live topology. |
| P10 — Trust | TLS/hostname/chain validated trusted broker/vhost, nonempty validated user-id, no impersonator/unverified federation. Map actual broker-qualified subject through native projector; runtime and topology credentials/permissions are separate. |
| P11 — Dispatch | Authorize before effects/disclosure, claim/lock inbox before handler, run scoped handler outside native callback, commit business/inbox/outgoing/reply atomically, then acknowledge. Unknown never asserts success or rollback. |
| P12 — Retry | One outbox scheduler, 5 scheduled ordinals including initial, 1s/5s/30s/2m bounded jitter. Fresh post-rollback disposition owner reacquires expected state; preserve a gap winner and one unique targeted chain. Crash reinvocation is not a new ordinal. |
| P13 — Terminal | Atomic canonical Terminal plus terminal outbox before source ack. Invalid/conflicting/untrusted messages use separate rejection responsibility and cannot poison legitimate inbox state. Malformed/oversize paths require verified native at-least-once DLX or leave unacked and stop visibly. |
| P14 — Administration | Explicit authorized inspection/maintenance/redrive bounded to 256 records; payload excluded by default. Default history/dedup indefinite. Redrive preserves identity and records explicit audited generation, never resets automatic exhaustion silently. Preserve unresolved provenance/replies. No Operations cleanup. |
| P15 — Resource limits | 256KiB payload default, configurable <=1MiB; 16KiB metadata; <=32 headers/8KiB. Consumer 8/prefetch16 per subscription, process work32, shared owned encoded bytes32MiB; admission precedes copies. Native buffers are separately documented. |
| P16 — Lifecycle | 10s publication deadline/30s reservation, capped60s transient publication backoff, 1s idle poll+wake. Close admission, cancel local waits, settle scopes/transactions, then publishers/broker and existing dependencies. Native recovery alone owns Rabbit reconnect/topology recovery. |
| P17 — Requests | Query or DurableCommand registration; reserve <=256 waiters/register before staging. 30s default/5m max monotonic entry deadline. Disposable ticket retains early response; commit confirmation required before waiting; no hidden caller commit. |
| P18 — Replies | One opaque requester endpoint per runtime, normal acknowledged queue, 10m unused expiry/max reply age >=max wait+margin. Validate actual responder, current attempt, original message and contract. Unauthorized/unmatched replies cannot complete waiters. Old destination expiry never reexecutes Operations. |
| P19 — Outcomes | Separate submission, exchange, protocol and typed OperationDecision facts. Local timeout/cancellation does not promise remote rollback. Fresh request keeps operation identity and returns original result to the current permitted attempt/route. |
| P20 — Observations/testing | Seven finite streams including explicit failed duration outcomes (435 combinations at 16 aliases); quiet normal logs and bounded repeated diagnostics. Real SQL/broker proofs, actual exporter and package checks per part, bounded CommonTesting and current consumer skill. |

## Necessary scope and evidence limits

The simplest accepted design is one transaction-participating store, one immutable
envelope, one publisher and dispatcher, then a bounded local ticket registry over
that same outbox. SQL and broker integration are necessary to establish the
specified guarantee. A generic bus framework, new DI/Configuration/security/
telemetry stack, proxy, worker platform, scheduler, portal, new tutorial or test
harness would expand responsibilities and is excluded.

Focused existing fixture composition satisfies the example alternative. CountryData
keeps its database-free default. All tasks include tests, docs, scoped pinned
formatting and independent review. Existing real PostgreSQL, native commit-failure
seams, package consumer and actual Windows/Ubuntu runtime routes are reused.
Single-node broker restart/persistence does not prove multi-node quorum failover.
Any unavailable Linux SDK-backed package or deployment proof remains unverified.

The initial doctor found one pre-existing invalid catalog registration pointing
outside the selected docs root (`cislib.contract.operations`), plus 303 historical/
setup warnings. Remove that invalid catalog entry while preserving the portable
contract and its guides. This scoped import repair does not adopt broad starter,
security-suite, quality-policy or catalog campaigns.

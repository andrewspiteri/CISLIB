---
title: RM-07 existing-contract reconciliation
type: document
status: Draft
change_id: CIS-0024
---

# Existing-contract reconciliation

| Existing fact | RM-07 implementation consequence |
| --- | --- |
| RM-06 is merged at actual `494e8941`; the attachment predates it. | Use the real trusted capture/writer and original Messaging outbox; no replacement hook or Audit store. |
| RM-03 canonical Text normalizes NFC. | Actor/resource scope and exact semantic metadata use strict raw UTF-8 Bytes; preserve exact identities and changed input. |
| RM-03 authorizes, checks Existing before handler and has separate lookup capabilities. | Replay original result before old-ETag handler and preserve Found/Replayed despite new read-owner cleanup. No coordinator change. |
| Native RM-03 Rejected retains OperationError(code,message), without a typed result payload. | The unreleased Management result envelope has an explicit result/rejection union. Preserve the actual native rejection and original completion through replay/lookup; do not fabricate metadata, revision or decision timestamps, or encode rejection as native Succeeded. Existing typed response variants remain readable. Active CSharp/TypeScript tasks own this integration correction within delegated limits. |
| Native Audit delivery requires the selected Messaging runtime and real broker Startup. | The service requires explicit immutable RabbitMQ bootstrap, an isolated broker native profile and the existing PostgreSQL outbox. Migration remains separate. Later broker outage preserves committed outgoing responsibility; no fake transport or destination archive is added. |
| AuditOperationReference has name/scope/key fields matching current RM-03 bounds. | Preserve actual registered operation name, versioned actor/resource scope and original key; no transport/key substitution. |
| RequireCisCapabilities needs a capability; native resource denial is normally403. | Use native auth-only policies plus the actual scoped authorizer for filtered/hidden resource reads and404; registration uses TargetOnly plus exact trusted self mapping. |
| Current workload profiles reject human/delegation. | Explicit independently validated Native integration supports operators; forged headers/serialized contexts confer nothing. |
| Native REST exposes per-call response mapper within its owning deadline, and bound clients forbid auth replacement. | Decode expected problem/durable facts in a per-call mapper; use the actual configured owner, preserving native status/security/cleanup without a second deadline. |
| REST closes admission before Heartbeat drains; Audit/Jobs already have drain barriers. | Management cutoff signals its actual work before joining; close is best effort only while transport allows it. No successful-close prerequisite or reorder. |
| JobCatalog Inspect defaults to100 and Jobs bounds can be raised explicitly. | Page all declared jobs without constructing handlers; reject an unrepresentable full snapshot rather than truncating it. |
| PostgreSQL receipt precision differs from reported seven-fraction timestamps. | Retain original reported timestamp strings independently from DB receipt time taken after locks. |
| One maximum inventory can consume4MiB. | Use byte-admitted whole-snapshot pages up to8MiB, not100 eagerly materialized maxima. |
| Only C# is present; machine global TypeScript is4.9.4. | Deliberately pin TypeScript5.9.3 as compiler dev dependency and native Node24.15.0/npm11.12.1; no runtime packages or new runner. |
| Npgsql10.0.4 and optional native Security/RabbitMQ/Metrics packages already exist. | Product app can reuse current pins without extending Core or source mappings. |
| CountryData is the maintained tutorial. | Management is the separate real product application requested by the source, not another tutorial or fixture mode. |

All15 acceptance criteria remain; unavailable actual infrastructure is reported
truthfully. No genuine product contradiction requires a new decision at intake.

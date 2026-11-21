---
title: RM-03 requirements and contract reconciliation
type: document
status: Proposed
cis:
  stable_id: cislib:change:cis-0018:reconciliation
---

# Contract reconciliation

The full source is preserved in
[planning/operations/sources](../../../planning/operations/sources/CISLIB_RM03_Durable_Operations_Implementation_Prompt.txt).
The source names implementation work, but the user's current request authorizes
intake/reconciliation/planning. No approval, product code or native proof is inferred.

## Existing contracts retained

| Existing fact | RM-03 consequence |
| --- | --- |
| ILocalTransactionContext has database/isolation/rollback metadata, no completion authority. | Validate named database/isolation and pass that original context. Never complete its owner. |
| UseAdoNetAsync is exclusive/non-reentrant and poisons escaping callback faults. | Separate claim/read and terminal-write callbacks; handler stays outside. Poison after-claim failures between callbacks with a safe operations.* reason, preserving original faults and the first existing reason. Actual reason bound is128 printable ASCII characters. |
| Owner completion guards callbacks, not the full RM-03 handler interval. | Caller must await Execute before completing/disposing. Do not advertise a whole-handler RM-02 guard. |
| Native commit failure after invocation is Unknown; later rollback cannot resolve it. | Never retry commit or call rollback again on a terminal owner. Recovery uses fresh authoritative lookup and explicit same-key resubmission through uniqueness. |
| LocalApplicationIdentity.Capture projects canonical EnvironmentOptions identities. | Use exact existing Application/Environment only; exclude InstanceId and deployment/credentials/trace. Do not renormalize those identity values. |
| ICallerContextAccessor.Current is a projection; ICapabilityAuthorizer evaluates established native state. | App resolves trusted logical scope and actual resource; existing authorizer gates Execute/replay/lookup before journal access or result disclosure. Do not trust serialized callers or request-body tenants. |
| Actual target type is CapabilityTarget, not AuthorizationResource. | Build it from captured server application/environment and app-resolved resource type/id. Execute/lookup capability requirements are explicit nonempty lists, at most8 each. Await authorization/audit faults normally; only ordinary logger/metrics faults are guarded. |
| Core/CommonTesting have no PostgreSQL provider dependency; IntegrationTests already pins Npgsql10.0.4 privately. | SQL-specific adapter uses BCL ADO.NET and DbException.SqlState. No new package/source mapping, EF, Testcontainers or provider runtime package is planned. |
| RM-02 composition/catalog/binding already bounds its three transaction streams. | Add only operation attempt/lookup timers, with one captured host binding. RM-02 remains the persistence/duration observer. |
| Existing package fixture requires SDK10.0.401 children; Ubuntu has runtime only. | Actual Windows package proof plus both available runtime lanes; Linux SDK-backed package proof remains a stated gap unless an existing supported route becomes available. No SDK-install campaign. |

## Concrete portable profile

Identity is five exact values: trusted server application, environment, trusted
application scope, registered operation name, opaque idempotency key. Equality is
ordinal/case-sensitive; no trimming/case folding. Scope is nonempty valid UTF-8,
at most256 bytes, with no NUL/control/unpaired surrogate. Operation aliases match
RM-02's64-character ASCII alias shape and registration is bounded to16 names.
Keys are1–128 visible ASCII bytes, opaque and case-sensitive, without automatic
replacement. Application/environment retain their existing255-scalar bounds
(up to1020 UTF-8 bytes each). The five columns total at most2488 data bytes.

A definition captures database alias, stable fingerprint/result contracts and
versions, trusted scope/resource resolution, capabilities, owned normalization,
codec/readers, finite limits and retention. Registration copies configuration,
does not construct scoped application dependencies or perform I/O, and rejects
duplicate/conflicting definitions. Execute capabilities must cover both submission and returning a retained business result; a handler-only permission check is insufficient. Caller-sensitive services use ordinary scoped
DI in the existing provider. No default allow-all authorization is selected.

Use one non-generic module-wide ConditionalWeakTable keyed by the original context,
holding only a tiny atomic used marker with no strong context back-reference.
Validate/authorize before spending admission; every admitted replay/conflict/
failure also spends it. Never remove/reset the marker. Cross-instance,
cross-definition and cross-generic calls share it. No growing strong dictionary,
ambient owner or per-attempt durable history is introduced.

Execute dispositions: Prepared, Replayed, KeyConflict, ResultExpired, Incompatible,
Contended, RetryRequired, Denied and RollbackOnly. Prepared/Replayed alone carry
the separate Succeeded/Rejected business decision. Cancellation and unexpected
programming faults propagate. A rejection is a completed business decision; its
valid audit/business effects commit with that transaction.

GetResult outcomes: Found, NotFound, ResultExpired, Incompatible, Unavailable and
Denied. A fresh authoritative ReadCommitted owner is created through the existing
factory, queried and actually settled/disposed. Optional ReadScopeCompletion reuses
TransactionCompletion for that read owner's cleanup facts. Its NotCommitted
describes the new read scope, not the original operation. Known committed Found
is retained despite read cleanup diagnostics. Unexpected query/codec faults
propagate with secondary cleanup unable to replace them. No row observed while
an original claim is active does not establish rollback.

## Fingerprint and result fidelity

The trusted operation normalizer produces an owned immutable business snapshot
plus bounded canonical inputs. The handler receives that normalized snapshot,
not a mutable original whose meaning can change after hashing. The server computes
SHA-256; callers cannot submit authoritative fingerprints.

Proposed shared frame v1 is small typed framing, not arbitrary JSON serialization:
ASCII domain prefix CIS.OPERATIONS.REQUEST followed by NUL, unsigned16 big-endian
frame version1, length-prefixed stable fingerprint contract identifier/version,
then a bounded ordered field sequence. Each value is a one-byte type tag followed
by an unsigned32 big-endian payload length and payload. Exact tag values and the
contract/version integer encoding are fixed with shared vectors in WORK-001
before dependent code starts.

- Missing and null have distinct tags/empty payloads; the operation explicitly
  decides which missing fields receive semantic defaults.
- Text is NFC with strict UTF-8 and no BOM. Opaque bytes retain their exact values.
- Integers use one canonical signed decimal representation: no plus sign,
  leading zeros or negative zero.
- Decimal values use sign, normalized coefficient and scale; trim coefficient
  trailing zeros while reducing scale, and encode zero as positive/coefficient0/
  scale0. The v1 .NET decimal range and operation-specific numeric constraints
  are documented rather than accepting culture/exponent-dependent spellings.
- Sequences retain meaningful order. Maps require unique normalized text keys
  sorted by UTF-8 byte order. No reflection-driven field selection or rules engine.
- Bound nesting to8, aggregate values to256 and any container to64 entries.
  Whole preimage defaults to64KiB and may be configured only up to1MiB.

Every meaning-bearing field and deterministic semantic default belongs in the
operation-specific contract. Trace/transport fields do not. Capture at most4
explicit fingerprint comparison versions per definition; versions are not part
of identity. Historical comparison must use the registered corresponding rules,
or return Incompatible. Never bypass a retained identity after an upgrade.

Vectors retain exact preimage hex and expected SHA-256 for Unicode equivalence,
decimal equivalence/negative zero, missing/default/null, sorted maps versus
ordered sequences, changed business input and ignored trace data. Shared framing
bytes are finalized/tested in WORK-001; this intake runs no hash-vector experiment.

Results use an explicit stable contract/version, bounded encoder and at most4
registered readers. Copy persisted bytes; decode those same bytes for Prepared
and replay so generated identifiers/values stay original. Default payload limit
is256KiB, configurable up to1MiB. Error codes are nonempty printable ASCII up to128
characters; business messages are bounded to1024 UTF-8 bytes. No CLR assembly/type
identity is persisted. Unsupported reader/expected decode incompatibility never
executes the handler; unexpected decoder faults preserve the original exception.

## Journal, expiry and concurrency

The production adapter supports PostgreSQL ReadCommitted in v1. Reject a supplied
RepeatableRead/Serializable context before claim/handler, without downgrading its
isolation. Commands default to15 seconds (configured1–300) with supplied
cancellation, actual provider settlement and no claim of a hard termination
deadline. Do not mutate session/transaction lock_timeout or issue transaction
control SQL. Map claim-phase55P03 to Contended only after escaping/poisoning;
40001/40P01 can become RetryRequired after the same boundary. Later55P03 and other
unexpected provider/timeout faults propagate with rollback-only. No inner retry.

Installation SQL uses ordinary logged tables, deterministic C equality, non-
deferrable unique full-identity columns, valid UTF-8/byte bounds, and schema version1.
Require UTF-8 PostgreSQL with block size at least8192 bytes so the bounded composite
index fits; installer detects unsupported storage/version conditions explicitly.
Do not hash away complete identity uniqueness or inherit locale-dependent equality.

Only an INSERT ON CONFLICT DO NOTHING RETURNING winner invokes the handler.
On conflict, a subsequent SELECT observes the committed winner under a new
statement snapshot. A winner removed by concurrent eligible maintenance between
those statements produces bounded contention/fresh-attempt guidance, never a
handler or a statement loop. An internal uncommitted claim is conditionally
finalized exactly once; an unexpectedly committed incomplete/corrupt row is
Unavailable/Incompatible and keeps protection, not a public Pending job.

Record database-generated preparation/recording timestamps at finalization,
not alleged commit times. Result/protection retention default to indefinite.
Optional finite expiries are persisted per row; later policy changes affect new
rows only. Protection cannot expire before full-result retention. Apply logical
payload expiry at every read, independently of maintenance timing.

Comparison precedence: unavailable/corrupt identity binding blocks execution;
unsupported fingerprint comparison gives Incompatible; comparable mismatch gives
KeyConflict; matching expired payload gives ResultExpired; otherwise read the
original through a supported result codec. Expiry never means absent. Elapsing
a protection-retention timestamp only makes a row eligible for explicit deletion;
until actual deletion it still arbitrates and protects the key.

Separate IOperationRetentionMaintenance is privileged, explicitly invoked and
disabled without nonempty configured maintenance capabilities. Authorize one
operation/trusted scope before SQL or count disclosure. It owns one fresh RM-02
transaction and processes at most256 rows with deterministic ordering/row locks;
SKIP LOCKED is allowed only here. Its result includes actual TransactionCompletion,
so candidate deletion counts do not masquerade as confirmed after Unknown.
Clearing expired payloads retains identity/fingerprint. Full eligible deletion
ends protection. Add no timer, background worker or implicit all-tenant sweep.

## Observation profile

Only two conditional timers: cis.operation.execute.duration with operation ×13
fixed outcomes (208 combinations), and cis.operation.lookup.duration with
operation ×9 (144). Histogram counts supply attempt counts. No key/scope/tenant,
fingerprint, business-code, SQL, payload or exception-message dimensions.

Execute outcomes: prepared_succeeded, prepared_rejected, replayed_succeeded,
replayed_rejected, key_conflict, result_expired, incompatible, contended,
retry_required, denied, cancelled, failed, rollback_only.
Lookup outcomes: found_succeeded, found_rejected, not_found, result_expired,
incompatible, unavailable, denied, cancelled, failed.

Resolve a known registered alias before capture; never label unknown input.
Capture a complete host binding before authorization and use it through actual
method-owned cleanup. Guard ordinary diagnostics without swallowing authorization/
audit faults. Retain mandatory central labels and existing duration buckets.
With RM-02 selected the proposed profile is26 streams/3808 configured combinations,
versus24/3456, within unchanged global limits. Validate actual mixed composition,
disabled settings, explicit small profiles, owner mismatch and both registration
orders in WORK-005. Normal calls stay quiet by default; failures use safe bounded
summaries without duplicating native transaction errors.

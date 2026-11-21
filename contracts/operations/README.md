---
title: Durable operation contract
type: contract
status: Draft
---

# Durable operations and idempotency

`CIS.Operations` is the portable contract for a short transactional operation
journal. Its implementation composes the existing explicit RM-02 transaction,
existing capability authorization and application-owned provider/source.
The guarantee is at most one committed protected effect for one retained scoped
identity. A handler may execute again after rollback. External HTTP, files, mail,
broker delivery and unrelated memory changes are outside that database guarantee.

The Core coordinator is selected with `AddCisOperations`, alongside passive
`AddCisOperation` definitions and an explicitly selected `IOperationJournal`.
Registration opens no database and installs no schema. The bounded implementation is tracked under
[CIS-0018](../../docs/changes/CIS-0018/implementation-plan.md). Registration must
never provide throwing service placeholders or silently install a database.

## Identity and authority

The full identity contains, in order, server application, server environment,
trusted application scope, registered operation name and caller idempotency key.
Equality is exact ordinal/case-sensitive: no trimming or case folding. Application
and environment preserve the existing `LocalApplicationIdentity` values and
255-Unicode-scalar bounds. Instance, deployment version, tracing and credentials
are excluded. Scope is nonempty valid UTF-8, at most 256 bytes, without controls,
NUL or unpaired surrogates. Operation aliases match
`[A-Za-z0-9][A-Za-z0-9_.-]{0,63}`. Keys contain one to 128 visible ASCII bytes
(`0x21` through `0x7e`) and remain opaque.

`OperationRequest<TRequest>` and `OperationLookupRequest` carry requested scope
text. They do not grant authority. A trusted application `IOperationScopeResolver`
uses the established caller projection and actual server resources to produce
`OperationScope`. Resolve aliases to stable canonical scopes, and never reuse an
old scope for a different authorization boundary. The resolver receives no
authority merely from a serialized `CallerContext`. The existing
`ICapabilityAuthorizer` must check all configured capabilities against a target
constructed from captured server application/environment and the resolved exact
resource. Authorize before submission, replay or lookup disclosure.

Definitions capture one to eight explicit execution capabilities covering both
submission and returning a retained result, and one to eight lookup capabilities.
Maintenance has a separate optional privileged set; empty means disabled.
Authorization/audit faults are real faults, not guarded logging failures.

## Public application boundary

`IDurableOperationService.ExecuteAsync<TRequest,TResult>` takes the original
`ILocalTransactionContext`, a named request and an application handler.
`GetResultAsync<TResult>` takes a named lookup, with no caller-owned write context.
Normal DI resolves registered definitions and scoped application services; there
is no public application claim/complete lifecycle, second provider or ambient
transaction selector.

`OperationDecision<TResult>` distinguishes Succeeded(value) and Rejected(code,
message). Rejections are original completed business decisions, not infrastructure
failures. Codes are nonempty printable ASCII up to 128 characters; messages are
valid UTF-8 up to 1024 bytes without NUL. They are business data, never metric labels.

Execution dispositions are Prepared, Replayed, KeyConflict, ResultExpired,
Incompatible, Contended, RetryRequired, Denied and RollbackOnly. Only Prepared and
Replayed carry a business decision. Prepared means all claim, handler, encoding
and terminal-write phases completed inside an **uncommitted** application
transaction. Replayed means a matching original committed result was observed
without invoking the handler; its durability does not depend on committing the
new read/arbitration transaction.

The application alone commits and inspects the existing `TransactionCompletion`.
Committed remains committed despite cleanup diagnostics. NotCommitted means
commit was never invoked. Unknown remains uncertain after any later rollback;
never retry commit, generate a replacement key or treat absence as proof of
rollback. Explicit resubmission uses the same scoped identity and business inputs
in a fresh transaction, arbitrating through database uniqueness again.

Lookup dispositions are Found, NotFound, ResultExpired, Incompatible, Unavailable
and Denied. Found alone carries the original business decision. A lookup uses a
fresh authoritative ReadCommitted owner, awaits its read and actual cleanup, and
never completes a caller's owner or disposes the application's source. Optional
`ReadScopeCompletion` describes that new read owner's completion. Its expected
NotCommitted or cleanup errors do not downgrade a known original committed Found
result. A missing result while an original claim is uncommitted proves nothing
about that transaction's eventual outcome.

## Normalization and canonical request frame v1

The trusted operation-specific normalizer receives the raw input and already
resolved `OperationScope`. It returns an owned immutable typed business snapshot
and at most four explicit canonical comparison inputs. The handler receives that
snapshot and original transaction context. Core copies canonical containers and
opaque bytes; it does not reflectively deep-clone arbitrary application objects.
Normalizers must isolate any mutable typed fields from the caller and bind
business fields to the resolved actual resource. Include every meaning-bearing
input/default in the chosen fingerprint contract; exclude transport-only data.

`OperationCanonicalFrame.Encode` emits these bytes in order:

1. ASCII `CIS.OPERATIONS.REQUEST` followed by one NUL byte.
2. Frame version `1` as unsigned 16-bit big-endian.
3. Fingerprint contract identifier length as unsigned 32-bit big-endian, then its
   exact ASCII bytes. Identifiers match
   `[A-Za-z0-9][A-Za-z0-9_.:/-]{0,127}`.
4. Positive contract version as unsigned 32-bit big-endian, limited to signed
   32-bit positive values in this .NET contract.
5. One canonical root value.

Every value is one tag byte, an unsigned 32-bit big-endian payload byte length,
then that payload. Counts and lengths describe encoded bytes, not UTF-16 units.

| Tag | Meaning and payload |
| --- | --- |
| `0` | Missing; empty payload. |
| `1` | Explicit null; empty payload, distinct from missing. |
| `2` | Boolean; one byte, zero or one. |
| `3` | Signed 64-bit integer; minimal invariant decimal ASCII, optional minus, no plus/leading zeros/negative zero. |
| `4` | Decimal; sign byte zero/one, scale byte, then minimal unsigned coefficient ASCII. Uses a 96-bit coefficient and scale0–28. Remove coefficient trailing zeros while reducing scale; zero is sign0/scale0/coefficient0. No exponent or locale-dependent spelling. |
| `5` | Text; NFC Unicode, strict UTF-8 without BOM. Invalid surrogates are rejected. |
| `6` | Opaque copied bytes; no text conversion. |
| `7` | Sequence; unsigned 32-bit entry count followed by encoded values in meaningful order. |
| `8` | Map; unsigned 32-bit entry count followed by encoded text-key/value pairs. Keys are unique after NFC and sorted by their unsigned UTF-8 bytes. |

Missing/null/default meaning is operation-specific. The shared frame does not
silently default fields or infer financial rules. Depth is at most eight including
the root, aggregate values at most256 (including encoded map keys), and each
container at most64 entries. A whole frame defaults to64KiB and can be configured
only up to1MiB. The server computes SHA-256 of these exact bytes; `GetHashCode`,
caller digests and default arbitrary JSON serialization are not authoritative.

The [shared vectors](../../conformance/operations/v1/request-fingerprints.json)
retain directly specified preimage hex and independently calculated SHA-256
literals. They cover missing/null, numbers/decimal zero, NFC equivalence, opaque
bytes, sequence/map ordering, deterministic example defaults, changed business
inputs and excluded tracing. Fingerprint contract/version is persisted alongside
the digest but is **not** part of uniqueness. Historical comparison uses explicitly
registered corresponding canonical inputs or returns Incompatible.

## Result codecs and retained records

`IOperationResultCodec<TResult>` supplies a stable current result contract, one to
four copied readers of the same contract, bounded success encoding and explicit
expected decode incompatibility. Do not persist CLR type/assembly identity.
Unexpected codec faults propagate and never permit reexecution. Copy encoded
bytes and decode the same representation for Prepared and replay, preserving
original generated identifiers and values. Success null is permitted only when
the chosen codec supports it. Rejections retain their original portable error
fields. Payload defaults to256KiB and is configurable only up to1MiB; overflow
before Prepared requires rollback of protected changes.

`IOperationJournal` is an infrastructure adapter port, not an application execution
API. It receives only the supplied context and portable identity/fingerprint/
encoded-result/retention values; no PostgreSQL or borrowed native handle appears
in the port. A claim reports acquired, committed existing row, disappeared winner,
known contention or fresh-transaction retry requirement. The adapter cannot
invoke a handler, complete an owner, replay statements or retain native handles.
Expected mapped claim faults are reported only after native failure has escaped
and poisoned the transaction. Finalization must affect exactly one own incomplete
claim; it never overwrites a completed original.

Retained row snapshots keep the original fingerprint/version, finalization/
corruption flags, original result when available and database-observed UTC times.
Incomplete or corrupt committed rows block execution. Recorded time precedes
commit and is never named an observed commit timestamp. Logical payload expiry
uses database observation time even before maintenance clears bytes.

Default result and duplicate-protection retention is indefinite. Finite positive
durations are captured for new rows only; protection cannot expire before the
full result. Clearing payload leaves its identity/fingerprint tombstone. Elapsing
protection retention only makes the whole row eligible for explicit privileged
deletion; protection continues until actual deletion. Deletion ends that guarantee.
Unsupported fingerprint comparison precedes KeyConflict; comparable mismatch
precedes matching ResultExpired. Unsupported result versions and failed decoding
never execute the handler.

The [coordinator guide](../../docs/modules/operations/core.md) covers execution,
fresh lookup and application ownership. PostgreSQL storage, explicit bounded
maintenance, operational observations and package qualification are separate
owned parts of this same implementation. CountryData remains the sole maintained tutorial,
with its database-free default; no delivery worker or RM-04 dispatcher is implied.

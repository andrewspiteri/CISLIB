# Messaging and reliable delivery

RM-04 has one portable envelope and delivery model, implemented in two ordered
parts: reliable events/commands, then typed request/reply. Request/reply reuses the
same production outbox, inbox, authorization and dispatch. It does not add a
second idempotency coordinator or persist a caller's Task.

The runtime and portable types use `CIS.Messaging` in `CIS.Core`. The production
PostgreSQL store uses BCL ADO.NET and application-owned data sources. The optional
RabbitMQ package owns its native client and broker integration. Core consumers
must acquire neither RabbitMQ.Client nor Npgsql through this module.

## Four independent facts

| Fact | Meaning |
| --- | --- |
| Staged | Validated immutable outgoing responsibility exists inside the original active local transaction. |
| OutboxCommitted | An actual known committed local transaction secured the outgoing record. |
| Confirmed | The selected broker supplied acceptance and required routing evidence for one native attempt. |
| Processed | A receiver secured its participating local effects, processing disposition and required outgoing/reply responsibility. |

None implies the later facts. A broker confirmation is not business success.
An RM-03 `Prepared` result is not committed. `Replayed` recovers an original
committed result, while a new inbox/reply transaction still needs its own commit.
Local wait timeout/cancellation does not establish remote cancellation or rollback.

## Immutable v1 wire

One envelope is a strict UTF-8 JSON object. The writer uses the following ordered
camelCase fields; optional fields are omitted. Readers preserve the exact full
wire bytes rather than reserializing received envelopes.

| Field | Portable representation and meaning |
| --- | --- |
| `envelopeVersion` | Integer `1`. Unknown required envelope versions are rejected before a handler. |
| `messageId` | Nonempty UUID in hyphenated `D` form, stable for this immutable logical envelope. |
| `kind` | `event`, `command`, `request` or `reply`. |
| `messageType`, `schemaVersion` | Stable name and positive integer version. Names contain 1–128 ASCII letters, digits, `.`, `_`, `-` or `/`. |
| `producerApplication`, `producerEnvironment` | Bounded claimed metadata, never proof of authority. |
| `createdAtUtc` | Canonical RFC 3339 UTC subset: `yyyy-MM-ddTHH:mm:ssZ`, optionally with one through seven fractional digits before `Z`. The writer emits seven digits. This is not a commit or ordering timestamp. |
| `payload` | One bounded JSON value encoded by an explicit DTO codec. Original numeric lexemes remain intact. |
| `requestAttemptId` | Nonempty UUID required for requests/replies, absent for events/commands. |
| `replyRoute` | Request-only object with `application`, `environment`, `endpointId`. The endpoint UUID is provider-generated and resolved through permitted templates, never an arbitrary queue/URL. |
| `inReplyToMessageId` | Reply-only nonempty UUID identifying the original request. |
| `operation` | Optional command/request/reply object with `operationName`, `requestedScope`, `key`, validated under existing Operations rules. Scope text grants no authority. |
| `correlationId`, `causationId` | Optional bounded diagnostics and causing MessageId, excluded from business idempotency. |
| `headers` | At most 32 bounded string pairs, at most 8 KiB combined decoded UTF-8 key/value bytes. No header grants authority. |

Requests require both `requestAttemptId` and `replyRoute`; replies require
`requestAttemptId` and `inReplyToMessageId`. Other kinds cannot carry those fields.
Events cannot carry an operation reference. Query/durable-mode policy additionally
validates the operation reference against the registered request definition.

The v1 reader rejects unknown envelope/structured-reference fields rather than
guessing whether an unknown field is required. Additive tolerance requires an
explicit later documented contract. Duplicate property names are rejected at
every nesting level, including equivalent escaped spellings. Reject malformed
UTF-8/Unicode, comments, trailing JSON, missing values, invalid identifiers and
excessive depth before invoking handlers. Stable registered codecs interpret
payload schemas; CLR names, assembly identities, delegates and native objects
never appear as serialization discriminators.

Payload size defaults to 256 KiB and is configurable up to 1 MiB. Wire metadata
is independently limited to 16 KiB, including escaping. Payload depth defaults
to sixteen, configurable up to thirty-two. Typed codecs use explicit portable
representations for currency/precision-sensitive values; Core does not convert
all JSON numbers through binary floating point.

`EncodedMessage` owns one exact wire array. Its envelope payload is a slice of
that same private array. SHA-256 over all wire bytes binds immutable transport
content; it is **not** the RM-03 normalized business fingerprint. Public copy
methods return application-owned detached copies. Runtime/provider copies must
be admitted under their actual shared encoded-byte budget before allocation.

The decoded payload slice covers the JSON value token, retaining its internal
lexemes and spacing. Whitespace surrounding that token remains unchanged in the
full wire body and counts as envelope framing metadata under the 16 KiB limit.

Delivery ordinal, target subscription, record purpose, redrive generation and
OutboxRecordId are provider-owned metadata outside the immutable envelope. A
retry/redrive preserves the original MessageId and bytes; each outgoing handoff
gets its own record ID. A reply is a new envelope with its own MessageId.

## Transaction and duplicate boundaries

All participating store work receives the original `ILocalTransactionContext`.
Only its owner commits, rolls back and disposes. Native callbacks remain
sequential; handler orchestration and broker waits occur outside them. Await the
whole handler/operation before completing the owner. Exactly one RM-03 durable
attempt is permitted per original context; inbox bookkeeping is ordinary store
work, not another nested operation.

The inbox key is exact receiving application + environment + stable logical
subscription + original MessageId. Instances share that identity; independent
event subscriptions each receive their own. A command/request route has one
logical subscription whose instances compete. No global/per-ledger ordering is
promised. Process, deployment, retry, trace and credential identities are excluded.

Acquire database uniqueness/locking before effects, compare the original digest,
and secure application changes, inbox completion, applicable operation result
and outgoing/reply records in the same local transaction. Acknowledge only after
known committed responsibility. `NotCommitted` and `Unknown` stay distinct.
A missing fresh read after Unknown cannot prove rollback; a new attempt must
reacquire the same unique identity. Supported guarantees cover participating
local effects and retained protection, not arbitrary HTTP/email/files/other DBs.

## Publication and durable dispositions

The store reserves at most 32 metadata records using database-time owner/version
fencing. Bodies are loaded separately after byte admission and a current fence
check. Commit the reservation before native publication; await the broker outside
SQL; record its actual outcome through a fresh short transaction. Never hold a
database transaction while awaiting the broker.

Confirmed requires publisher confirmation plus selected routing checks. Unknown
has transient classification and always retains recoverable identical bytes.
Permanent configuration/unroutability is retained as Blocked. Fencing prevents
stale database updates; it cannot prevent a delayed former owner transmitting.
Receiver arbitration handles repeated delivery.

There is one retry scheduler: a future-eligible outgoing record targeting the
failed subscription. After rollback, reacquire the same processing key in a
fresh short owner and compare its expected ordinal/generation/digest/provenance.
A concurrent successful or already secured later disposition wins. Atomically
secure RetryScheduled/Terminal plus exactly one handoff before acknowledging the
source. Internal retry/redrive record IDs must match retained handoff identity
under the lock; original verified operation scope and exact resource must match
current trusted resolution.

Five scheduled ordinals are the default, including the initial one, with 1s/5s/
30s/2m delays and bounded jitter. A crash can revisit an ordinal, so this is not a
lifetime physical-handler-invocation cap. Business rejection is a completed
decision. Broker outage does not consume this handler retry budget.

Invalid/conflicting/untrusted deliveries use a separate rejection identity and
cannot overwrite or poison canonical processing. Malformed/oversize input uses
only a verified safe native quarantine path. If responsibility cannot be secured,
leave it unacknowledged and visibly stop affected-consumer admission. Never join
global consumer shutdown from within its admitted callback.

## Security, retention and request/reply

The selected provider supplies actual native authentication evidence to the
existing independent `SecurityHostContextScope`; serialized producer metadata
and retained caller data never authenticate. Apply existing capability checks to
the actual resolved resource before effects or result disclosure. Retry under
the current retry service's authority, not impersonation of the saved producer.
Retained provenance is audit data; reply destinations need current permission.

History and protection default to indefinite. Explicit bounded administration
exposes safe metadata without payloads. Pending/Reserved/Blocked/unresolved/
scheduled responsibility and dependent provenance/reply records are not deleted
for capacity relief. Payload cleanup can retain identity/digest tombstones.
Finite full-protection deletion requires declared delivery and supported redrive
horizons. Explicit redrive preserves identity and records an audited generation;
null redrive horizon means indefinite while required protection remains. It
never reopens successfully completed work transparently. Operations retention
and privileged maintenance remain independent.

Typed requests register Query or DurableCommand. Reserve finite caller capacity
and register the waiter before staging/publication can occur. A staged disposable
ticket retains early responses and never secretly commits its caller's owner.
`WaitForReplyAsync` requires an exact receipt observed through a fresh independent
read; absent/unresolved submission returns SubmissionNotConfirmed promptly.
Ticket expiration/disposal releases local capacity without deleting durable work.

Wait deadlines are monotonic, start at method entry and include submission
settlement. Defaults are 30 seconds, maximum five minutes, with 256 outstanding
local waits. One reusable requester endpoint is isolated per runtime. Its finite
unused expiry/obsolete reply age defaults to ten minutes and covers maximum wait
plus delivery margin. Old endpoint retirement never reexecutes Operations.

Authenticate expected responder, permitted route/type/version, exact attempt and
original request before completing a waiter once. InvalidResponse applies only
after expected responder and attempt are established. Unauthorized, unmatched,
late and duplicate replies cannot complete/cancel a legitimate different waiter.
A fresh durable request attempt keeps the original RM-03 identity and returns its
original authorized result to the current attempt/route without repeating the
committed effect. Separate submission, exchange, protocol and `OperationDecision`
facts; expired/incompatible/denied results never authorize reexecution.

The [Core guide](../../docs/modules/messaging/core.md) links actual APIs and
foundation tests. [Wire vectors](../../conformance/messaging/v1/README.md) establish
portable framing only. Real transaction/broker/authentication/recovery evidence
belongs to the focused provider/consumer tests and CIS-0019 verification, not to
the existence of these contracts or public test scripts.

# Transactional staging and publication

[Audit](../audit/README.md) reuses these exact public publication ports with its
single common Event route. Its source capture is distinct from decoded portable
data. Stage on the original participant; source-specific refusal/fault poisons
that usable context even before Messaging. A deliberate second submission keeps
AuditId but obtains a new MessageId; transport retry preserves existing IDs/bytes.
Audit adds no source-side occurrence deduplication or archive receipt.

`IMessageOutbox` stages through the original `ILocalTransactionContext`; it never
commits or disposes its caller's transaction. `PublishAsync` selects a registered
event route and `SendAsync` selects a registered command route. Both use the
captured explicit DTO codec, validate its current round trip and strict wire
framing, then store one owned immutable envelope. The original context must use
the selected database and ReadCommitted isolation.

The returned staging receipt identifies the outgoing record, MessageId and exact
SHA-256 digest. It proves neither commit nor broker acceptance. Serialization,
store and inconsistent-receipt faults mark the context rollback-only while
preserving the original exception. Known byte/storage capacity refusal returns
Overloaded and also prohibits an apparently successful atomic caller commit.
Staging accepted before shutdown is joined; shutdown never takes completion
ownership of a foreign transaction.

`IMessagingClient.PublishAsync` and `SendAsync` prepare bounded immutable bytes
before opening their short submission owner. This prevents unlimited callers
opening database work while waiting for encoded-buffer capacity. After staging,
they inspect actual RM-02 completion and return OutboxCommitted, NotSubmitted or
Unknown independently of any broker or business result. Stable staged IDs and
actual cleanup/completion survive an uncertain commit. Only a known committed
submission issues a coalesced wake hint; a failed hint cannot redefine persistence.
Expected configured storage exhaustion or a transient store failure returns
NotSubmitted only after the owner actually settles with NotCommitted.

When request definitions are selected, the existing hosting bridge attaches the
typed request coordinator once to the same outbox, client and dispatcher before
startup. A composition without request support returns defined Unavailable
before submission. Use `RequestAsync` for a short submission owner, or
`StageRequestAsync` and its disposable ticket with an application-owned original
context. Wait only after that owner commits. Query/durable reply semantics and
current-attempt recovery are described in [typed requests](requests.md) and the
[portable contract](../../../contracts/messaging/README.md).

## One bounded publisher operation

The existing host owns scheduling. `MessageOutboxPublisher.PumpAsync` performs one
bounded batch and starts no background loop in its constructor. Its default idle
wait is one second, with a coalesced wake signal and one owned idle wait per
publisher. Normal polling emits no success log.

1. Open a fresh short ReadCommitted owner, reserve at most 32 **metadata-only**
   rows with database-time fencing, and inspect actual reservation completion.
2. Start no body read or broker operation unless that reservation is known
   Committed. Uncertain reservation work remains recoverable after lease expiry.
3. Admit at most the shared process publication limit, initially four. Reserve
   twice the encoded length before loading a body: the current SQL read/strict
   decoder can overlap two encoded buffers. A stale fence returns no body.
4. Settle the read owner before invoking the native publisher. The adapter borrows
   immutable Core bytes through its actual native completion, with no extra
   optional owned publication copy and no database transaction held open.
5. Record Confirmed/Rejected/Unknown through a new fenced short owner and preserve
   that owner's actual completion. A native confirmation remains a native fact
   even when the subsequent Published-state commit is Unknown.

The batch result contains bounded current attempt facts and actual reservation,
read and settlement completions; it is not a history. Its selected/reserved count
alone never proves a committed reservation. `UpdatePrepared` means that the
conditional store update was staged; inspect SettlementCompletion before treating
its state as durable. Native attempt counts remain distinct from deferred capacity
and stale-fence observations.

Unknown acceptance always retains transient classification and identical bytes.
Known unroutable/configuration failures retain the record as Blocked rather than
discarding it. Transient publication backoff is exponential with bounded jitter,
capped at sixty seconds and applied against database time. It is independent of
scheduled handler retry ordinals. Capacity deferral stages a recoverable transient
state without loading a body or inventing a native publication attempt. A stale
former publisher can still transmit; database fencing rejects stale updates and
receiver duplicate protection handles repeated logical delivery.

Publication's ten-second deadline requests cooperative cancellation. The original
native call is still awaited, with borrowed bytes held until actual settlement.
Cancellation after possible transmission is Unknown, never assumed rejected.
Fresh settlement uses an independent bounded cooperative cleanup token. Genuine
programming faults are rethrown after best-effort Unknown settlement; failed
settlement leaves the fenced row recoverable rather than replacing that fault.

## Shared resource and shutdown ownership

The module shares one process budget across runtime roots: default 32 admitted
deliveries, 256 request waiters, four publications and 32 MiB owned encoded bytes.
Active runtimes must select consistent numeric limits. The byte-wait and tiny
buffer-lease object counts are also finite. A cancelled byte wait releases its
waiting registration, never a buffer already used by another operation.

Explicit staging reserves a conservative four maximum-envelope overlap before
codec allocation. Its internal envelope adopts the codec-owned array instead of
making another payload copy. Admission fails promptly when existing delivery
work already holds the available bytes, avoiding a nested allocation deadlock.
Providers borrow immutable data only for the awaited operation; typed application
objects and documented native/runtime overhead remain separate from the encoded
buffer capacity claim.

Close caller/consumer admission first, settle accepted staging, handlers and their
owners, then stop publishing and dispose native transport resources. Publisher
stop cancels current cooperative waits and joins actual work. An expired timeout
or caller cancellation cannot release a still-borrowed body. Resource-owner
disposal closes its admission and waits for its live leases/waiters; profile reset
occurs only after every owner and lease settles. Data sources and metric owners
remain alive until their dependants drain.

The [resource tests](../../../implementations/csharp/tests/CIS.Core.UnitTests/Messaging/MessageResourceBudgetTests.cs),
[staging tests](../../../implementations/csharp/tests/CIS.Core.UnitTests/Messaging/MessageOutboxTests.cs)
and [publisher tests](../../../implementations/csharp/tests/CIS.Core.UnitTests/Messaging/MessagePublisherTests.cs)
exercise these logical boundaries using existing CommonTesting transaction scripts.
They establish no SQL atomicity or real broker persistence. Actual PostgreSQL and
RabbitMQ acceptance, platform limits and current executed results belong to
[CIS-0019 verification](../../changes/CIS-0019/verification.md).

[Combined publication tests](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Messaging/MessagePublicationIntegrationTests.cs)
use both existing private per-case fixtures through the actual PostgreSQL store,
Core staging/pump and RabbitMQ transport. They cover original-context visibility,
commit/rollback, exact broker wire bytes, fresh receipt/state inspection and a
controlled Unknown result after a real broker confirmation. That final seam
simulates lost confirmation; it is not a network-fault injection or quorum
failover proof. Capture callbacks acknowledge test observations only and do not
stand in for the production inbox/handler dispatcher. Authored cases alone are
not executed evidence or completion of Parts A/B.

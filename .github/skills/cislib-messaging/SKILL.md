---
name: cislib-messaging
description: Implement, use or review CISLIB transactional messaging, RabbitMQ delivery, typed request/reply and durable-operation result recovery. Apply to CIS.Messaging and its optional provider; application transaction and business-operation ownership remain with their existing modules.
---

# CISLIB messaging and reliable delivery

Read [repository guidance](../../../AGENTS.md), the
[portable contract](../../../contracts/messaging/README.md) and
[Core ownership guide](../../../docs/modules/messaging/core.md).
For staging/publication use [publication](../../../docs/modules/messaging/publication.md);
for receiving use [dispatch](../../../docs/modules/messaging/dispatch.md).
For typed requests, provisional tickets and original-result recovery use
[requests](../../../docs/modules/messaging/requests.md).
For retries, explicit redrive or retention read
[failure handling](../../../docs/modules/messaging/failure-handling.md).
Read [PostgreSQL installation](../../../docs/modules/messaging/postgresql.md),
[RabbitMQ trust/topology](../../../docs/modules/messaging/rabbitmq.md) and
[hosting/drain](../../../docs/modules/messaging/hosting.md) when composing a runtime.
Use [observations](../../../docs/modules/messaging/observations.md) for Metrics
and bounded diagnostics. Follow links to actual compiling consumers and recorded
verification; authored source alone is not native evidence.
Use [testing](../../../docs/modules/messaging/testing.md) for public substitutes,
outside-friend packages and the distinct provider evidence boundaries.

Keep Staged, OutboxCommitted, broker Confirmed and receiver Processed separate.
Stage through `IMessageOutbox` using the original explicit RM-02 participant;
the application completes its owner. `IMessagingClient` convenience submission
owns a short transaction. Native callbacks stay sequential and contain no broker
or reply waits. Commit a publication reservation before awaiting the broker;
do not wait for a staged request inside the transaction that must commit it.
Installation and topology are
explicit application/deployment actions, never first-use side effects.

Acquire inbox uniqueness before protected effects. Await the handler and its
cleanup outside sequential native callbacks, then atomically secure application
changes, inbox, applicable RM-03 result and required outgoing/reply responsibility.
Invoke RM-03 once on that original context. Prepared is not committed; Replayed
still needs the new inbox/reply transaction to commit. Acknowledge known secured
responsibility only. Unknown and a missing independent lookup do not prove rollback.

Reuse exact wire bytes and MessageId for transport retries. New caller attempts
use new MessageId/RequestAttemptId while keeping the same operation identity and
business input. Reply routes, tracing, credentials and local deadlines do not enter
the business fingerprint. Changed business meaning conflicts with the original
operation; expired/incompatible/denied results never authorize reexecution.

Use actual native broker evidence and the existing independent Security scope.
Headers, stored CallerContext and provenance are not delegated authority. Retry
services require their own current capabilities and exact retained binding;
returning an original result also requires current resource and recipient checks.
Conflicting or untrusted input has separate rejection state and cannot poison a
legitimate inbox or complete a legitimate waiter.

Register a bounded local waiter before staging. For application-owned transactions,
dispose unused `MessageRequestTicket<T>` and call `WaitForReplyAsync` only after
known committed submission; the API must not secretly commit that owner. The
monotonic deadline covers entry through submission and waiting, while actual
owned cleanup must settle. Timeout/cancellation ends local waiting, not remote
execution. Recover through a fresh authorized attempt and the same RM-03 identity,
with a response addressed to that current permitted attempt. Never persist Tasks.
Validate replies against current native responder authority and the exact attempt,
endpoint, aliases and SHA-256 request-wire digest; business normalization is a
separate binding. Selecting requests reserves reply work/byte headroom inside the
existing process totals, rather than increasing those totals.

Keep one outbox retry scheduler, finite work/byte/wait bounds and actual owner
drain before dependencies. Core has neither RabbitMQ.Client nor Npgsql; the
optional provider owns broker integration and applications own data sources.
CommonTesting scripts establish deterministic caller facts, not SQL atomicity,
authentication or broker durability. Keep labels finite and normal operation quiet.
Preserve CountryData's database-free tutorial; use the existing named provider and
public-package fixtures. Single-node restart proof is not quorum failover,
deployment or release qualification.

# Messaging Core contracts and ownership

`CIS.Messaging` lives in the existing Core assembly. Its explicit ports separate
immutable staging, actual broker acceptance, transactional receiving and local
typed waiting. Applications retain their RM-02 transaction/data-source ownership.
See the [portable contract](../../../contracts/messaging/README.md) for exact wire,
identity, retry and uncertainty semantics.

## Definitions and explicit codecs

Use `IMessageCodec<T>` with a stable `MessageContract`. `Encode` returns UTF-8 JSON
for one explicit DTO schema; `TryDecode` reports expected incompatibility without
inventing a value. Preserve genuine programming faults. Codecs are application-
owned and thread-safe; stable contracts, rather than CLR names, identify data.

`MessageRouteDefinition<T>` fixes an event fan-out or command single-service
route. `MessageSubscriptionDefinition<T>` captures the scoped business-handler
factory, typed actual-resource resolver and one to eight required capabilities.
The resolver sees independently established caller data; request headers do not
grant scope. Handler work receives `MessageHandlerContext.Transaction`, the
original participant, and runs outside native callbacks.

`MessageRequestDefinition<TRequest,TResponse>` captures Query/DurableCommand mode,
request/reply codecs and the expected responder application/environment. It
registers caller contracts without constructing a server handler. Register the
separate `MessageRequestSubscriptionDefinition<TRequest,TResponse>` for responder
work. Durable mode binds an existing Operations alias and also supports a command
without a reply waiter. Its handler returns existing
`OperationDecision<TResponse>.Succeed(value)` or `.Reject(code,message)`; the
dispatcher owns integration with the existing RM-03 coordinator.

Passive metadata methods are `ConfigureCisMessaging`, `AddCisMessageRoute`,
`AddCisMessageStore`, `AddCisMessageSubscription` and `AddCisMessageRequest` in
`CIS.Messaging.Hosting`. They capture a `MessagingSnapshot` without opening a
connection, installing schema, creating handlers or starting workers. They do
not register throwing placeholder runtime services. The existing host selects
the actual runtime/provider separately. All four registries admit at most
sixteen unique ordinal aliases. Build a new provider after changing metadata.

`MessagingSnapshot.Validate()` checks the complete passive graph, including
exact type/contract/database consistency, one logical receiver per command
route and the exact responder request-definition instance. Events permit
independent subscriptions. A `MessageStoreDefinition` maps its bounded alias to
the application-owned RM-02 database registration. Handler factories/resource
resolvers are invoked only inside real delivery processing.

## Wire and bytes

`MessageEnvelope` takes bounded ownership of an encoded JSON payload and headers.
`MessageWire.Encode` produces `EncodedMessage`; `TryDecode` reports finite
`MessageWireFailure` for invalid untrusted input before a handler. Its strict v1
reader rejects unknown fields, duplicate keys at every depth, malformed UTF-8,
invalid request/reply field combinations, excess nesting and size. The timestamp
profile requires `T`, seconds and `Z`, with whole seconds or one through seven fractional digits.

`EncodedMessage` owns one private exact wire buffer; payload metadata slices that
same array. `CopyBytes`, `CopyPayload` and `MessageDigest.CopyBytes` detach public
application-owned copies. `CopyTo` lets an optional adapter copy into already
admitted storage without exposing private mutable arrays. Internal runtimes reuse
their borrowed immutable memory and account temporary encode/materialization/
publication overlap before allocating. The default shared encoded-buffer budget
is 32 MiB; native-client/runtime overhead is separately documented and bounded.
Configured capacity admits a conservative four-envelope staging overlap; this
does not replace runtime byte accounting or claim a measured memory footprint.

Full-wire SHA-256 is a transport-content conflict binding, not a business
fingerprint. Never include request attempt, reply endpoint, trace, credentials or
delivery ordinal in RM-03 normalized business input. Forwarding an existing
outgoing row reuses exact bytes, MessageId and digest.

## Submission, receiving and local waits

`IMessageOutbox` stages through an explicit original participant. Its receipt
proves staged responsibility only. `IMessagingClient` owns short convenience
submission transactions and reports `MessageSubmission` separately from a broker
or business response. Preserve actual `TransactionCompletion` and stable IDs on
Unknown; do not retry commit or infer rollback from a missing later read.

`IMessagingStore` is an infrastructure participant, never a transaction owner.
Metadata-only reservation precedes owned-byte admission and the separate fenced
body read. Broker publication occurs outside database callbacks/transactions.
Claim/complete and fresh retry/terminal disposition methods enforce database
arbitration. Low-level public request records are data, not validation or
authorization proofs; stores validate bounds, nulls, enums, expected identities
and state before I/O. Inspection/maintenance affect at most 256 rows and expose
safe metadata rather than payloads by default.

The trusted optional adapter supplies `MessageNativeEvidence` from its actual
validated broker path. The existing native projector must validate its private
ticket evidence. No `CallerContext` constructed from headers establishes an
authenticated scope. `MessageProvenance` records original audit facts; current
retry authority and exact retained operation/resource binding are checked again.

`MessageRequestTicket<T>` is a disposable local reservation with immutable
`MessageRequestReference`, not a persisted continuation or a transaction owner.
Reserve/register before staging to retain fast responses. Verify committed
submission through a fresh independent read before waiting. Dispose unused
tickets; finite deadlines are a fallback. Timeout/cancellation/disposal releases
local resources and preserves remote uncertainty.

`MessageRequestResult<T>` separates `MessageSubmission`, `MessageExchangeOutcome`,
`MessageResponse<T>` protocol and optional original `OperationDecision<T>`.
`InvalidResponse` contains no invalid typed value. A fresh attempt carries the
same operation reference with new request/correlation identities. Replayed
results still require the new inbox/reply unit to commit before acknowledgement.

## Current focused checks

The compiling [definition tests](../../../implementations/csharp/tests/CIS.Core.UnitTests/Messaging/MessageDefinitionTests.cs)
demonstrate passive setup, exact registration and event/command identities.
[Contract tests](../../../implementations/csharp/tests/CIS.Core.UnitTests/Messaging/MessageContractTests.cs)
exercise independent persistence/protocol/business facts and public ticket
substitution. [Options tests](../../../implementations/csharp/tests/CIS.Core.UnitTests/Messaging/MessagingOptionsTests.cs)
cover coupled bounds and finite-retention horizons.
[Wire tests](../../../implementations/csharp/tests/CIS.Core.UnitTests/Messaging/MessageWireTests.cs)
consume [independent portable vectors](../../../conformance/messaging/v1/README.md).
Their authored source alone establishes no passing result; actual observations
and provider evidence are recorded in
[CIS-0019 verification](../../changes/CIS-0019/verification.md).

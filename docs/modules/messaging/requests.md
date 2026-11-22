# Typed requests and original result recovery

Register a `MessageRequestDefinition<TRequest,TResponse>` with explicit portable
codecs, its command route, the expected responder application/environment and a
`Query` or `DurableCommand` mode. Register the responder through
`MessageRequestSubscriptionDefinition<TRequest,TResponse>`. These definitions
extend the same outbox, dispatcher, Security scope and optional broker adapter;
they do not introduce another transaction owner or retry engine.

A query handler returns `OperationDecision<TResponse>` without creating a durable
operation result. A durable command names an existing RM-03 operation definition.
The dispatcher invokes that operation once in the original message transaction.
Application changes, inbox completion, its original operation result and outgoing
reply responsibility commit together. A business rejection is a completed typed
decision, distinct from a protocol denial or incompatible result.

```csharp
MessageRequestResult<Receipt> result = await client.RequestAsync<Order, Receipt>(
    new("SubmitOrder", order, new("SubmitOrder", requestedScope, idempotencyKey),
        timeout: TimeSpan.FromSeconds(15)), cancellationToken);

if (result.Outcome == MessageExchangeOutcome.ResponseReceived
    && result.Response!.Disposition == MessageProtocolDisposition.Completed)
{
    OperationDecision<Receipt> decision = result.Response.Decision!;
    // Inspect the independent Succeeded/Rejected business decision.
}
```

`RequestAsync` owns one short local submission transaction. It registers a
provisional bounded waiter before staging and returns submission persistence
separately from the exchange outcome. `OutboxCommitted` proves outgoing
responsibility committed; it proves neither broker confirmation nor remote
success. An uncertain native commit returns `SubmissionUnknown` with its stable
reference. A timeout after submission retains `OutboxCommitted` and ends only
the local wait. There is no automatic new business request after uncertainty.

For an application-owned transaction, call
`IMessageOutbox.StageRequestAsync<TRequest,TResponse>(context, request)`, complete
the original owner and then use `IMessagingClient.WaitForReplyAsync(ticket)`.
Dispose every unused ticket. The wait reads the exact receipt through a fresh
independent Read Committed owner and awaits actual cleanup. An uncommitted or
missing row returns `SubmissionNotConfirmed`; that observation does not commit
the original owner or establish that an uncertain transaction rolled back. A
provisional ticket remains usable after a later known commit while its original
deadline and capacity remain available.

The monotonic deadline starts at API entry and includes local admission,
submission, independent receipt confirmation and response waiting. Caller,
deadline and shutdown cancellation retain the first identified source. An
unrelated application `OperationCanceledException` remains the original fault,
including when cancellation happens concurrently. Cancellation closes admission
promptly; ticket disposal and runtime stop still join actual transaction, timer,
codec-buffer and native cleanup.

## Current authority and exact response correlation

Each running root has one random requester endpoint, resolved through configured
application/environment namespace templates. The recipient cannot select an
arbitrary queue or exchange. The responder checks current native identity,
business target capabilities and the explicit reply capabilities before exposing
an original result. A fresh request uses new `MessageId` and `RequestAttemptId`,
while preserving the operation identity and business input. RM-03 returns the
original result or the finite `KeyConflict`, `ResultExpired`, `Incompatible`,
`Denied` or `OutcomeUnknown` protocol fact. Expiry and denial do not authorize
the business effect to run again.

Reply admission requires the actual reserved reply ingress, expected native
responder application/environment, current reply permission, exact original
message/attempt identity and the current endpoint. Its strict JSON wrapper also
echoes `requestAlias`, `routeAlias` and `requestDigest`: the lower-case SHA-256 of
the **actual immutable request wire received by the responder**. This binds a
safe denied response to that exact input, including before canonical business
claim. A changed-body request cannot cause its denial to complete another input's
waiter. This digest is correlation evidence, separate from the normalized RM-03
business fingerprint.

The wrapper has `responseVersion: 1`, `protocol` and either a typed completed
decision (`decision`, `value` or `error`) or the applicable finite protocol fact.
Only a response passing every native and correlation gate may produce
`InvalidResponse` for incompatible typed content. Unmatched, untrusted and
changed-digest replies are quarantined and cannot complete a legitimate waiter.
No CLR type name, stored caller context or claimed transport header grants
authority.

Denied initial requests retain separate rejection and reply responsibility;
they do not poison a legitimate canonical inbox. Canonical terminal and rejected
replies are linked to their fresh transition in the same native transaction.
Successful and replayed commands commit the new inbox and current reply before
acknowledgement. A broker acknowledgement failure after that commit never
reclassifies the handler as failed.

## Bounds and ownership

All active roots share the existing process work, publication, waiter and encoded
byte budgets. Selecting requests reserves one work slot and `4 ×
MaximumEnvelopeBytes` for reply ingress **inside** those totals. Ordinary business
work, administration and publication cannot consume that lane. A request profile
requires at least two delivery slots and `12 × MaximumEnvelopeBytes` total
encoded capacity. Responder encoding reserves its bounded working bytes before
effects; local submission releases preparation after actual settlement and before
waiting, so saturated self requests can still receive their replies.

An early response keeps its owned encoded-byte lease until consumption or ticket
disposal. The charge covers both its retained immutable wire and the codec input,
including codecs that return `ReadOnlyMemory<byte>`. On consumption the typed
value becomes application-owned. Disposal, expiry and stop clear unconsumed
private values even if the public ticket remains reachable. A known capacity
refusal returns `Overloaded` with the existing submission fact; it does not
invent a rollback.

Host shutdown cancels local waits before waiting for business dispatch to drain,
then joins actual request, consumer, scope and native ownership before releasing
dependencies. No pending `Task`, waiter or caller authority is persisted.

The current PostgreSQL store requires schema **2**, installed explicitly with
`PostgreSqlMessagingSchema`; schema 1 is rejected. Envelope wire version remains
1. Current installation is not a migration service. See [PostgreSQL](postgresql.md),
[hosting](hosting.md), [failure handling](failure-handling.md) and
[testing](testing.md) for installation, retention and proof boundaries. The named
RPC child fixture is solely the bounded B05 process-death/public-package consumer;
it is not a maintained application or general test harness.

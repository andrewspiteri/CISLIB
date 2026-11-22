# Failed deliveries and administration

The dispatcher awaits the original transaction and its cleanup before selecting
failure handling. A known rollback of an admitted handler can schedule another
ordinal. Fresh bookkeeping uses the configured database and ReadCommitted
isolation, reacquires the same protected processing identity, and stages either
RetryScheduled plus one targeted retry outbox record, or Terminal plus one
targeted quarantine record. The source can be acknowledged only after that
fresh owner's actual completion is Committed.

Concurrent success in the rollback gap wins. Failure bookkeeping preserves
Handled and already secured later responsibility. A failed retry rolls back to
RetryScheduled for its current ordinal; only its matching retained handoff can
acquire work again. Repeating the failure callback cannot create another retry
chain. The immutable envelope, MessageId, digest, original provenance and full
resolved Operations binding stay the same across scheduled retries.

An original Unknown outcome, a fresh uncertain disposition commit, or unavailable
infrastructure leaves the source unacknowledged and requests consumer stop. No
automatic commit retry or assumption of rollback resolves uncertainty. A later
delivery uses database arbitration against retained state. Shutdown can requeue
without allocating another handler ordinal. Broker publication backoff belongs
to the publisher and does not consume scheduled handler attempts.

Each subscription captures a finite ordinal count and one delay per subsequent
ordinal. The default is five ordinals including the initial delivery. Jitter is
bounded by the configured ratio. A known terminal decision and exhaustion secure
the current ordinal as terminal responsibility. Only a newly secured canonical
terminal transition may stage its required safe protocol reply in that same
transaction; business handlers and Operations are never called by disposition
bookkeeping.

Unsupported contracts, denied input, conflicting content/provenance and invalid
ordinals use a separate rejection identity and a targeted quarantine outbox row.
They do not reserve or overwrite the legitimate canonical processing key. A
well-formed Reply on an ordinary event/command route follows this rejection path:
its exact wire is quarantine evidence, rather than a business reply addressed to
the wire's original requester. Securing that responsibility leaves the consumer
available for a later valid delivery with the same MessageId. A
conflict discovered during fresh bookkeeping first rolls back that owner, then
attempts one separate rejection transaction. Malformed or oversized bytes use the
provider's explicitly configured safe native dead-letter path, or remain
unacknowledged if transfer responsibility cannot be secured.

Retries authenticate as the current local retry service. Its current resource,
retry, Operations and reply permissions apply. Original producer facts are audit
evidence, never a credential or delegated caller context. An application's
additional requirement for live original-producer permission uses its registered
retry validator before execution or result replay.

## Explicit administration

`IMessageAdministration` exposes three bounded calls in an established Security
scope. Registration uses the existing messaging host. The server captures its
own application/environment and selected store; callers cannot choose another
owner identity or an arbitrary physical destination.

`MessagingOptions.InspectionCapabilities` and `MaintenanceCapabilities` default
to empty, which disables those entry points. Select explicit capability lists
before snapshot capture. Inspection and maintenance authorize the complete
`messaging.store` target whose resource identifier is the selected store alias.
Redrive uses the registered subscription's `RedriveCapabilities` against the
`messaging.subscription` target and that subscription's alias. Authorization
precedes every store call and metadata/count disclosure. Disabled and denied
responses have absent data, which does not represent an empty store.

```csharp
options.InspectionCapabilities = ["messaging.inspect"];
options.MaintenanceCapabilities = ["messaging.maintain"];

MessageInspectResult inspected = await administration.InspectAsync(
    new MessageInspectQuery(subscription: "Countries", messageId: messageId), token);

if (inspected.Disposition == MessageAdministrationDisposition.Executed
    && inspected.Processing is { Disposition: MessageProcessingDisposition.Terminal } terminal)
{
    MessageAdminRedriveResult redrive = await administration.RedriveAsync(
        new MessageRedriveCommand("Countries", messageId, terminal.Version, "operator.reviewed"), token);
    // Inspect redrive.Transition and redrive.Completion independently.
}
```

Inspection returns at most 256 safe metadata rows, without payloads, provenance,
operation keys or resolved operation scopes. An exact subscription/message query
also returns a safe current version and disposition. A page and exact observation
can change concurrently; redrive's expected version is checked again under the
store's transaction lock. Stale versions return Conflict.

Redrive only deliberately reopens retained terminal work. It preserves logical
identity, targets the same failed subscription, increments the explicit
generation, and applies both retained and currently selected supported horizons.
Handled effects cannot be reopened transparently. The actual initiating caller,
complete target/capabilities and safe operator code are passed to the existing
awaited Security audit hook after settlement. This hook is not a durable audit
queue. A hook fault remains visible even when the database already committed;
inspect the authoritative version before another attempt.
An owner-disposal fault is likewise propagated after cleanup is awaited, without
reclassifying an already known Committed transaction as a rollback.
Privileged redrive and maintenance still await the existing audit hook with the
actual settled outcome before propagating that original fault. If the audit hook
also fails, the earlier operation or cleanup fault remains primary.

Every admin call holds shared module work admission through authorization, SQL,
actual owner cleanup and audit. Redrive additionally reserves two portable maximum
envelopes before the store materializes historical bytes, even when a restart
selects a lower current payload limit. Shared capacity refusal or closing admission
returns Unavailable before SQL. The default 32 MiB byte budget accommodates this
bounded reservation; an explicitly smaller budget may refuse redrive.
The host's stopping cutoff refuses new admin entries immediately; calls already
accepted retain their original transaction, caller scope and drain ownership.

Maintenance is caller initiated, at most 256 total changes, and uses the captured
retention policy. It starts no worker and cannot modify Operations history.
Retention remains indefinite by default. Finite policies still preserve pending,
reserved, ordinary blocked and unresolved reply/provenance dependencies. Candidate
counts become `ConfirmedCounts` only after Committed; Unknown never reports a
confirmed zero. A retired obsolete reply is the narrow documented exception in
the [PostgreSQL guide](postgresql.md).

The focused unit cases reuse CommonTesting's bounded transaction scripts to
establish policy, authorization boundaries and actual scripted owner settlement.
They do not prove persistence. The existing isolated PostgreSQL fixture supplies
real SQL cases for rollback gaps, retry restoration, duplicate handoff suppression,
canonical-free rejection and versioned redrive. Its lost-commit-reply case injects
a reporting failure after a real successful native commit. Deterministic
administration authorizers in those SQL cases do not establish native broker
authentication or deployment proof.

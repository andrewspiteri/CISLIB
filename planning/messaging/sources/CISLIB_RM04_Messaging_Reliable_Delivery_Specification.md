# CISLIB RM-04 Messaging and Reliable Delivery

**Implementation specification — version 1.0 — 21 November 2026**  
**Delivery:** one specification, two implementation parts.  
**Part A:** reliable events and commands.  
**Part B:** typed broker request/reply, including durable operation result recovery.

## 1. Purpose and scope

Applications must be able to publish events, send commands, and obtain typed responses through one initial message broker integration. Database changes and outgoing messages must be recorded atomically. Repeated delivery must not repeat a protected committed local effect. A caller that loses a response must be able to recover the original durable operation result.

Implement C# first on the repository's .NET 10 baseline. Define portable message semantics and wire contracts for future languages. Reuse RM-01 Security, RM-02 Transactions, RM-03 Operations, existing hosting, Logging, Metrics, and CommonTesting.

### 1.1 Delivery boundaries

| Part | Required outcome | Completion boundary |
| --- | --- | --- |
| A — Reliable events and commands | Publish committed outgoing records; consume through the broker; handle concurrent duplicates, retries, terminal failures, restarts, and bounded resources. | Real database/broker evidence establishes the relevant publication and consumption guarantees. Part A is usable independently. |
| B — Typed request/reply | Send a typed query or durable command, correlate its response, bound the caller's wait, and recover a durable result after response loss. | Real integration evidence shows response-loss recovery without repeating the committed operation. |

Part B depends on Part A. Define the shared envelope and extension points during Part A so Part B does not introduce a second delivery framework. Part B implementation may begin against settled contracts, but its acceptance depends on the relevant Part A guarantees.

This specification does not include HTTP/gRPC adapters, streaming RPC, transparent remote-object proxies, cross-database transactions, sagas, durable application continuations, a scheduler, a management portal, automatic broker deployment, or a second broker integration. It does not provide a globally ordered event log or general per-ledger scheduling.

### 1.2 Concrete provider defaults

The initial broker for this specification is **RabbitMQ**, using its supported official .NET AMQP 0-9-1 client in a separate optional package. RabbitMQ is a proposed provider choice made for this specification; the previous roadmap did not select a broker. It is not a claim that the user already chose or deployed RabbitMQ. A later explicit provider decision should replace the provider-specific profile while preserving the shared semantics.

Use a supported RabbitMQ/client combination and pin its exact versions through the repository's dependency process at implementation time. The AMQP 0-9-1 profile gives this implementation concrete publisher-confirmation, routing-return, manual-acknowledgement, and validated-user-id semantics. Do not implement two protocols or a custom broker protocol. The official client remains supported; RabbitMQ also documents a newer AMQP 1.0 client, which is outside this initial profile. [R7]

The first durable store is PostgreSQL through BCL ADO.NET and the existing RM-02 participant context. PostgreSQL 18 is the current qualified RM-03 target. The application selects and owns its DbDataSource and native provider dependency. Core must not acquire Npgsql or the broker SDK. [C1, C2, C4]

## 2. Repository baseline and integration rules

The reviewed baseline is main commit **669d286711340e7f63398256cee27575932f58f4**. Recheck current main before implementation and preserve compatible changes made after that baseline. This is a specification, not evidence that RM-04 has been implemented. [C0]

### 2.1 Existing responsibilities

| Module | Existing responsibility | RM-04 integration |
| --- | --- | --- |
| RM-01 Security | Established caller identity, trusted scope resolution, capability authorization. | Establish broker-derived authority before executing handlers or disclosing results. |
| RM-02 Transactions | Explicit local transaction ownership and native participation; Committed, NotCommitted, and Unknown outcomes. | Use the original participant context for inbox, application, operation, and outbox work in one database transaction. |
| RM-03 Operations | One protected operation identity, normalized business input, stored original result, fresh authorized lookup. | Invoke its existing coordinator for durable commands and recover results through its existing semantics. |
| RM-04 Messaging | New module. | Own transport, production inbox/outbox, publication, dispatch, delivery retries, terminal delivery disposition, and reply correlation. |

At the baseline, RM-03's outgoing-record table is a test fixture. There is no production outbox schema, inbox, broker adapter, or dispatcher to reuse. RM-04 must implement those production components without treating fixture SQL as an existing public contract. [C3, C4]

### 2.2 Mandatory transaction and operation rules

1. Registration is passive. It opens no database connection, installs no schema, and starts no worker.
2. The transaction owner alone commits, rolls back, and disposes. Stores and handlers receive the original ILocalTransactionContext.
3. Native UseAdoNetAsync callbacks remain sequential. Run handler orchestration outside a native callback so dependent stores can perform their own sequential calls.
4. Await the whole IDurableOperationService.ExecuteAsync before completing its owner. **Prepared is uncommitted.** A Prepared business result must not escape as a committed success.
5. Only one RM-03 durable attempt is permitted per original transaction context. Inbox bookkeeping must be ordinary transactional store work, not a second nested RM-03 operation.
6. Replayed identifies an original committed result. The transaction that records a new inbox disposition and reply still needs its own confirmed commit before responsibility can be handed off.
7. Preserve Unknown after uncertain commit. Do not retry commit, replace the idempotency key, infer rollback from a subsequent missing read, or use later rollback success to resolve uncertainty.
8. A fresh, explicit attempt uses the original scoped identity and database uniqueness arbitration. RM-04 may schedule a delivery attempt under policy; RM-03 itself does not become an automatic retry engine.
9. Await actual owned callback, transaction, broker-operation, and scope cleanup. A timeout must not merely abandon those operations while claiming they have settled.

These rules derive from the existing Transactions and Operations contracts. [C2, C3]

## 3. Shared messaging contract

### 3.1 Patterns

| Pattern | Meaning | Delivery and response |
| --- | --- | --- |
| Event | A fact that already occurred. | Each configured logical subscription receives its own delivery. No business response is expected. |
| Command | An instruction to an owning service. | One logical destination, with competing instances and possible repeated delivery. |
| Request | A query or command requiring a response. | One logical destination plus a permitted reply route and a request-attempt identity. |
| Reply | A response to one request attempt. | Routed to that attempt's caller endpoint, carrying typed result or a defined failure outcome. |

Broker acceptance is not business success. Record three separate facts: staged in an uncommitted transaction; committed to the outbox; confirmed by the broker. Only a handled request response or authorized RM-03 recovery establishes the business outcome. [R1]

### 3.2 Namespace and packaging

Use the new proposed namespace CIS.Messaging for portable contracts and module runtime in CIS.Core. Use CIS.Messaging.PostgreSql for the BCL-based store following the existing Operations precedent. Put the RabbitMQ SDK and broker-specific authentication/transport integration in a separate optional CIS.Messaging.RabbitMQ project/package.

Preserve the approved Core dependency closure and the existing .NET SDK/runtime pins. Do not introduce a general messaging framework, custom DI container, alternate lifecycle manager, or new telemetry stack. [C1]

All new API names below are proposed capabilities. Reconcile exact spelling and composition with current repository conventions before implementation; do not represent these as existing APIs.

| Proposed surface | Required responsibility |
| --- | --- |
| IMessageOutbox | PublishAsync and SendAsync stage an immutable event/command using an explicit transaction context. StageRequestAsync stages a request and returns its ticket without waiting. |
| IMessagingClient | Convenience PublishAsync/SendAsync own a short transaction. RequestAsync owns its submission transaction and then waits. WaitForReplyAsync accepts an already committed staged-request ticket. |
| IMessageHandler<T> | Process an event/command using the delivery's DI scope, explicit context, and cancellation token. |
| IRequestHandler<TRequest,TResponse> | Produce a typed response under the registered query or durable-command mode. |
| Provider/store ports | Small, mockable infrastructure boundaries for confirmed publication, bounded delivery, explicit database participation, and durable dispositions. |

Expected business rejection and expected transport states are result values. Preserve genuine programming faults and invalid API use as exceptions according to existing CISLIB conventions. Handler exceptions must cause the appropriate transaction rollback and classified delivery handling; do not turn every exception into a business rejection.

### 3.3 Envelope and identities

Define one versioned UTF-8 JSON envelope. Register stable message names and versions; never serialize CLR type names, assembly identities, arbitrary runtime type discriminators, delegates, or native broker objects. Use explicit DTO codecs with documented compatibility rules.

| Field | Required meaning |
| --- | --- |
| EnvelopeVersion | Positive integer; initial value 1. Unknown envelope versions cannot invoke a handler. |
| MessageId | UUID identifying one immutable logical envelope. Preserve across publication retries, broker redelivery, and ordinary redrive. |
| Kind | Event, Command, Request, or Reply. |
| MessageType and SchemaVersion | Registered portable contract name and positive integer version. Names use 1–128 ASCII characters from letters, digits, dot, underscore, hyphen, and slash. |
| ProducerApplication and ProducerEnvironment | Captured producer metadata. These fields alone establish no authority. |
| CreatedAtUtc | RFC 3339 UTC timestamp. Not a commit timestamp or ordering guarantee. |
| Payload | Owned, bounded typed data encoded once at staging. |
| RequestAttemptId | UUID required for requests/replies; a new caller attempt gets a new value. |
| ReplyRoute | Configured logical endpoint alias for a request, authorized for the established caller. Never an arbitrary URL or unchecked queue name. |
| InReplyToMessageId | The request MessageId to which a reply belongs. |
| Operation reference | For durable commands: registered operation name, requested scope, and opaque caller idempotency key. The server resolves the full RM-03 identity. |
| Correlation and causation | Optional bounded diagnostic correlation identifier and causing MessageId. Neither is an idempotency key. |

RM-03's full identity remains server application, server environment, trusted resolved scope, registered operation name, and caller key. Preserve its exact comparison and validation rules. Request-attempt IDs, reply routes, tracing, delivery counters, and changing credentials must not enter the canonical business fingerprint. [C3]

Keep delivery attempt ordinal, target subscription, and internal retry/terminal identifiers in provider-owned delivery metadata, outside the immutable logical envelope. Each retry/terminal forwarding record has its own OutboxRecordId; it does not create a new business MessageId. A reply is a new message with its own MessageId.

Take ownership of encoded bytes before returning from staging. Retrying an existing outgoing record uses those exact bytes. Keep a SHA-256 digest of the immutable envelope for duplicate-content checks; do not reuse that transport digest as the RM-03 business fingerprint. The same MessageId with different immutable bytes is a conflict, never an instruction to overwrite or execute again.

Reject duplicate JSON keys, malformed UTF-8, invalid identifiers, excessive depth, unknown required fields, and unsupported required versions before handler invocation. Readers may tolerate explicitly documented optional additive fields. Currency amounts and other precision-sensitive values require an explicit portable schema representation; do not silently map them through binary floating point.

## 4. Part A — Transactional publication

### 4.1 Durable staging

Staging must serialize and validate before it reports success, insert the outgoing record through the supplied original context, and participate in the caller's transaction. A serialization or store failure must prevent an apparently successful atomic application commit; propagate/mark rollback-only as required by RM-02.

A staging receipt means only that work was recorded in the active transaction. The convenience client returns OutboxCommitted only after a known Committed completion; NotCommitted and Unknown remain distinct submission states and carry the relevant transaction completion. Return stable message identifiers even for an uncertain submission so the caller can inspect/reconcile without inventing a new identity.

Do not publish before commit. Outbox workers read only committed rows through their own transactions. Application consumers receive solely from the broker. This preserves the transactional outbox boundary. [R2]

### 4.2 Store and publisher lifecycle

The store must persist envelope bytes/digest, message and record identifiers, owning application/environment, logical destination, record purpose, creation/eligibility times, publication state, attempt count, and safe bounded diagnostic code. Store reservation owner/version and expiry using database time. Do not retain raw exception objects, credentials, SQL, or unbounded error histories.

Use the internal lifecycle Pending → Reserved → Published, with Blocked for retained records needing operator/configuration intervention. Expired reservations are eligible for recovery. Reserve a bounded batch atomically; use a fencing/version token so only the current reservation owner may change publication state.

Never hold a database transaction open while awaiting the broker. Commit the reservation, perform bounded broker publication, then record its result in a fresh short transaction. An old publisher may still transmit after losing its reservation; fencing protects database updates, while receiver idempotency protects repeated delivery.

The adapter reports Confirmed, Rejected, or Unknown. Confirmed requires the selected broker's acceptance evidence and route checks, not a successful socket write. Set Published only after Confirmed. A lost confirmation or uncertain Published-state commit leaves recoverable work; retry with the same MessageId and bytes. Transient broker unavailability uses capped backoff and never silently expires accepted durable work. Permanent malformed configuration/unroutability is retained as Blocked with a visible diagnostic. [R1, R3]

For events, subscriptions must be provisioned before publication when delivery is expected. Default publication requires at least one route. An explicit AllowNoSubscribers policy may treat no bindings as intentional, but that outcome must be named separately from Delivered/Processed. Confirmation cannot prove that every intended subscription was configured correctly; topology validation and deployment ownership remain explicit.

### 4.3 PostgreSQL ownership

Provide a production, versioned installation script and explicit installer for the messaging metadata, outgoing records, processing records, and required indexes. Use a messaging-owned schema version; do not reuse the Operations schema version. Runtime registration, first publish, and first consume must never install or mutate schema.

Use application-owned DbDataSource registrations, original RM-02 contexts, parameterized commands, and sequential native callbacks. Reserve dispatch work in short transactions, using the provider's appropriate locking primitives. Uniqueness and conditional updates must be enforced in the database.

Inspection/maintenance surfaces are bounded and explicitly authorized. They expose state and safe reasons, not message payloads by default. Do not create a portal, generic scheduler, or unbounded administrative export as part of RM-04.

## 5. Part A — Consumption, duplicates, and retries

### 5.1 Logical consumer identity

The duplicate identity is receiving application + receiving environment + stable logical subscription/handler name + original MessageId. It excludes process/instance ID, deployment version, and retry count. Different applications/subscriptions must each be able to process the same event independently. Instances of one logical subscription share duplicate protection. [R4]

The processing record stores immutable-envelope digest, trusted provenance/reference, current delivery ordinal and disposition, and the data required to safely complete retry or reply handoff. Bound all values. Terminal failure and successful business completion are different dispositions.

### 5.2 Transactional dispatch

For each admitted delivery:

1. Validate bounded framing and establish trusted producer identity. Apply the registered authorization policy before business execution or result disclosure.
2. Create the independent DI/security scope and a local transaction owner.
3. Acquire the processing identity through an atomic insert/lock under the unique constraint, before invoking business effects. Compare the immutable digest on repeats.
4. If already handled, suppress the effect and follow its recorded acknowledgement/reply disposition. If a retry is scheduled, use the ordinal rules below.
5. Run application work using the original context. A durable-command binding invokes RM-03 once and uses its normalized business input and decision.
6. Record successful processing, applicable operation result, outgoing events, and required reply in the same transaction as the local changes.
7. Await the whole handler/coordinator, then complete the transaction and inspect the actual outcome.
8. Acknowledge only after a known committed responsibility boundary. Dispose the delivery scope after all owned work settles.

If two instances receive copies concurrently, database arbitration must prevent two protected effects from committing. Do not implement check-then-act deduplication in memory. [R4]

On NotCommitted, protected local effects did not commit and may be retried under policy with a fresh owner. On Unknown, do not acknowledge successful processing or assume a failed operation. Recover through authoritative state and RM-03 where applicable. A new attempt must reacquire the same unique database identity; a missing standalone lookup is not proof that an earlier transaction cannot still commit.

The guarantee is bounded to effects participating in the same supported local transaction and to retained protection records. Arbitrary external HTTP, email, files, or another database require a separate idempotent/durable arrangement. A handler may execute again after rollback; the guarantee concerns committed effects. [C2, C3]

### 5.3 One retry mechanism

Use the messaging outbox as the initial durable retry scheduler. It stores a future eligible time and republishes through the broker to the **specific failed logical subscription**, not the original fan-out route. Consumers must never execute directly from that outbox.

After known failed/rolled-back business processing, secure the next disposition in a new short transaction:

- Reacquire the same processing key under database arbitration. Transition only the expected ordinal and nonterminal state. A duplicate may have committed successfully in the gap after rollback; never overwrite Handled, an already secured retry, or a later disposition.
- Update the processing record to RetryScheduled(n) and insert exactly one outgoing retry record for ordinal n.
- Preserve MessageId, immutable envelope, logical consumer identity, and original verified provenance reference.
- Use a uniqueness constraint over consumer/message/purpose/ordinal to prevent multiple retry chains.
- Acknowledge the original delivery only after this bookkeeping transaction is known committed.

An original or older-ordinal redelivery observes the already secured disposition and cannot schedule another independent retry. Only the expected ordinal is eligible to run the next attempt. A later ordinal without the corresponding trusted durable record is invalid. Failed or uncertain disposition commits remain unacknowledged until safely resolved.

The configured budget counts scheduled delivery ordinals, including the initial ordinal. A process crash before a disposition is secured can cause the same ordinal's handler to execute again; do not advertise the budget as a strict lifetime cap on physical handler invocations. Database arbitration still prevents repeated protected committed effects. Repeated crash cycles are visible through health/diagnostics and may pause the consumer; they must not silently discard the message.

Known business rejection is a completed decision, not an infrastructure retry. Unsupported schemas, authorization denial, immutable-content conflict, and exhausted scheduled retry ordinals have explicit terminal handling. Classify broker/publication failures separately from scheduled handler retries so a long broker outage does not consume that budget.

### 5.4 Terminal handling

Persist Terminal plus one terminal-message outbox record atomically before acknowledging the source. The terminal route is an access-controlled quarantine queue for that consumer, carrying original identity, bounded diagnostic code, and controlled payload access. If publication to quarantine fails, the pending outgoing record retains responsibility.

This transition applies only to an authorized canonical delivery whose expected processing state is still eligible. A conflicting digest or untrusted delivery with an attacker-selected MessageId must be quarantined in a separate rejection record. Preserve the legitimate processing state, original result, reply, and provenance unchanged; never let invalid input reserve or poison the canonical message identity.

Do not rely on an unconfirmed publish followed by source acknowledgement. Do not claim RabbitMQ's default dead-letter configuration guarantees lossless transfer; its stronger dead-letter mode requires specific configuration. This specification's application terminal path uses the transactional handoff above. [R5]

Malformed or oversize input that cannot enter the normal envelope store must still have a bounded, explicit quarantine path. Bound broker-side acceptance sizes and configure a verified broker-native safe dead-letter transfer for such input; if that path cannot secure responsibility, leave the delivery unacknowledged and stop the affected consumer with a visible health failure. Never allocate an unbounded copy or acknowledge and silently discard it.

Redrive is an explicit authorized operation on retained failed work. It preserves original identity and verifies that duplicate protection still applies. Reopening a terminal disposition is deliberate and audited; it is not ordinary delivery retry. Reprocessing a successfully completed effect as new business work requires a new explicit operation and is outside transparent redelivery.

## 6. Part A — RabbitMQ provider profile

Use durable exchanges and queues, persistent publications, publisher confirms, mandatory routing checks, and manual consumer acknowledgement. Commands target one logical service queue. Events use one durable queue per independent subscription. Replicas of a subscription compete on that queue. Use quorum queues for the durable reference profile and document the replication prerequisites separately from a single-node developer setup. [R1, R3, R5]

The adapter owns long-lived connections and bounded channels through existing host lifetime. Respect the native client's channel/concurrency and body-buffer ownership rules. Copy or decode delivered bytes before native buffers cease to be valid. Choose one recovery owner; do not combine native automatic recovery with an independent duplicate consumer-registration loop. [R7]

Provision topology explicitly or through an opt-in startup stage with appropriate credentials. Production runtime credentials must not require unrestricted broker administration. Validate topology compatibility; never delete/recreate live queues to resolve a mismatch.

Configure queue length/byte limits to reject new publications rather than silently evict accepted durable work. Configure native delivery-limit/TTL paths deliberately so they cannot bypass retained terminal handling. Do not require a delayed-message plugin: scheduled retries use the database outbox.

### 6.1 Ordering

The first implementation promises **no global or per-ledger business ordering**. A routing/correlation key alone does not establish ordering across concurrent publishers, retries, and instances. The common contract must not advertise an unsupported ordering guarantee. RabbitMQ documents that concurrency and redelivery can affect order. [R6]

The provider may expose serial queue consumption as a documented provider option, but that is not a new portable per-key ordering contract. Ledger serialization, sequence validation, or sharding remains explicit application responsibility. A consumer requiring stronger ordering must reject an incompatible configuration. Do not add a generic partition coordinator or distributed lock service to satisfy this module.

## 7. Part B — Typed request and reply

### 7.1 Registered modes

Register every request as either Query or DurableCommand. Query returns a read result whose freshness follows the application contract. A new query attempt may obtain a newer answer; do not promise a recoverable historical query snapshot unless it was explicitly retained. DurableCommand binds a registered RM-03 operation and requires its requested scope and idempotency key.

A durable command's normalized business payload excludes reply routing, request-attempt IDs, local wait settings, and other transport fields. Reusing the same operation identity with changed business meaning produces the existing RM-03 conflict outcome; it never overwrites the original result.

### 7.2 Submission and waiting

RequestAsync performs these steps: reserve bounded caller capacity; validate the route/codec; allocate MessageId and RequestAttemptId; prepare a local waiter; stage and commit the request in a short local transaction; then await a matching response within the remaining local deadline.

Register the waiter before publication can occur, so a fast response cannot race ahead of registration. Release reserved capacity and owned resources on every completion path. A known submission rollback completes the request as NotSubmitted. Unknown submission remains Unknown; retain the ticket/operation reference for recovery rather than guessing that it was never sent.

For application-owned transactions, StageRequestAsync reserves bounded caller capacity and registers its provisional waiter before staging, then returns a disposable ticket without waiting. The ticket retains an early response if it arrives after commit but before WaitForReplyAsync is called. Its finite deadline starts at staging entry; caller disposal after rollback, explicit disposal, or deadline expiry releases its local reservation. The caller must dispose an unused ticket; deadline cleanup remains a bounded fallback. Ticket expiry ends local waiting only and neither commits nor removes the caller's staged request.

WaitForReplyAsync must verify known committed submission through the store/receipt before admitting the wait; active/uncommitted or unresolved submission returns SubmissionNotConfirmed promptly. A missing read alone does not assert rollback. No API secretly commits the caller's owner. No waiting for a request inside the transaction that must commit before it can be published.

The local wait deadline starts at RequestAsync entry and covers admission, staging, and waiting; it does not replace actual settlement of owned work. A timed-out request can therefore return only after its submission transaction has settled sufficiently to report the correct persistence fact. Keep timeout origin and submission outcome distinct.

### 7.3 Result shape

Define a typed result containing RequestAttemptId, MessageId, optional operation reference, submission outcome, exchange outcome, optional typed business decision, and bounded error context. Avoid a single ambiguous Success flag.

| Exchange outcome | Meaning |
| --- | --- |
| ResponseReceived | A validated matching reply arrived. Inspect its business or protocol decision. |
| TimedOut | The local wait ended without a definitive response. Remote operation outcome can remain unknown. |
| Cancelled | The caller stopped waiting. This does not establish remote cancellation. |
| NotSubmitted | Submission is known not to have committed. |
| SubmissionUnknown or SubmissionNotConfirmed | Submission commit is uncertain, or a staged ticket has no confirmed commit yet. Preserve the ticket/operation identity; do not imply remote nonexecution. |
| Unavailable or Overloaded | A known local admission/configuration condition prevented acceptance, or the defined submission result explains any uncertainty. |
| InvalidResponse | The expected authenticated/authorized responder and exact request attempt are established, but its typed response content/version is invalid. Do not expose the invalid value. |

A response may carry business Succeeded(value) or Rejected(code,message), or a protocol result such as KeyConflict, ResultExpired, Incompatible, Denied, or OutcomeUnknown. Preserve RM-03 distinctions rather than representing them all as business rejection. Match existing OperationDecision factory/API names in implementation. [C3]

The caller's wait timeout is not a command business expiry. This initial scope does not add a portable execution-expiry policy. A later policy must define whether queued work may begin after a deadline and must still allow disclosure of an existing authorized original result. Do not implement wait timeout by silently expiring durable command messages.

### 7.4 Responder and saved results

For a new durable request, acquire its inbox identity and invoke the existing RM-03 coordinator once. When execution is Prepared, stage the reply and inbox completion in that same transaction and commit the whole unit. Acknowledge the request only when the business/result/reply responsibility is durably secured.

For a fresh request attempt referring to an already committed operation, use authorized RM-03 replay/recovery and create a reply for **the current RequestAttemptId and permitted ReplyRoute**. The new request's inbox record and outgoing reply must commit even though the original business result was already durable. Failure here leaves the original result intact and the new delivery recoverable.

For an exact duplicate envelope, do not rerun its effect or blindly drop a required reply. Use its retained processing/reply disposition to ensure the original reply remains secured. A genuinely new caller attempt uses a new envelope and correlation identity while retaining the same RM-03 operation identity.

ResultExpired, incompatible retained encoding, denied access, and corrupt retained state never authorize reexecution. RM-03's default indefinite retention and explicit maintenance remain unchanged. Messaging cleanup must not delete Operations records or start its maintenance. [C3]

### 7.5 Reply routing and restart behaviour

Use a reusable, bounded number of reply queues per active requester endpoint, not one queue per request. Isolate endpoints so another instance cannot consume and discard a reply belonging to an active waiter elsewhere. Route resolution is a validated local configuration and broker naming/permission profile, not a central discovery service. A reply alias contains the permitted application/environment namespace plus an opaque endpoint identifier generated by the provider. Resolve it through that caller's configured route template, with an allowlist of expected reply contracts.

The reference implementation uses normal acknowledged reply queues, not RabbitMQ Direct Reply-To. Direct Reply-To does not durably buffer replies and provides at-most-once reply delivery. [R8]

The waiting call is process-local. Restart recovery uses a new request attempt and the same operation identity; it does not resume a persisted Task or require the old process to consume its former reply. Configure finite unused-endpoint queue expiry and a matching maximum reply-delivery age, initially 10 minutes and never shorter than the maximum admitted wait plus the documented delivery margin. Do not expire an endpoint with a live consumer merely because it is idle. After retirement/expiry, an obsolete reply may become terminal ReplyDestinationExpired only through the documented retained disposition; it must not block the original committed operation or trigger its reexecution. Replies are the explicit exception to indefinite retry of durable commands: the result remains recoverable through a new operation request after the old wait endpoint expires.

Replies are validated against authenticated responder, expected route/type/version, request-attempt ID, and original request MessageId. An unauthenticated, unauthorized, unmatched, or incorrectly correlated reply is rejected/quarantined without completing or cancelling a legitimate waiter. InvalidResponse is available only after the expected responder and exact attempt are established. Complete a waiter once. Duplicate replies are harmless. Late replies release no new work, invoke no business handler, and are acknowledged/quarantined according to the validated reply policy with a bounded diagnostic.

When the process or network fails after the receiver commits, the operation result and reply outbox survive. A later caller attempt can obtain the same result without the previous waiter. Local wait cancellation removes the waiter and requests cancellation of still-owned local work; it does not promise to undo a submitted remote operation.

## 8. Identity, authorization, and reply protection

Reuse RM-01's existing independent SecurityHostContextScope and native projector path. Resolve scoped operation/handler services inside that established scope and await work before disposing it. There is no existing broker authenticator at the baseline; the provider must add the narrow trusted bridge. Do not replace caller accessors or create CallerContext objects from serialized headers and treat them as authority. [C5]

For the initial RabbitMQ trust profile, use TLS to a configured trusted broker/vhost, distinct mapped publishing principals, and RabbitMQ's validated user-id property. Require that property on accepted publications. Disable the impersonator tag for principals in this trust domain, and exclude unverified federation paths. Map the broker-qualified principal to the existing application/environment identity through the native security profile. Metadata claiming another producer does not override the established identity. RabbitMQ validates user-id against the authenticated connection user when it is supplied; missing properties and impersonator/federation exceptions must therefore be handled explicitly. [R9]

Broker permissions restrict publication, subscription, retries, quarantine, and reply routes to their registered owners. Subject grants use only identity/evidence actually established by this broker path; do not invent JWT claims, delegated human authority, or provider roles.

A retry is published by the retrying service, so its broker user-id will differ from the original producer. Accept internal retry provenance only when the authenticated retry publisher and restricted route match a retained local processing/outbox record, ordinal, and immutable digest. Establish the retry under that service's own current SecurityHostContextScope, with explicit retry and applicable operation capabilities. Resolve the original protected resource and exactly the same canonical operation scope from the trusted record; if this cannot be established, deny rather than create a different operation identity.

Keep the original producer evidence for audit. Retained CallerContext, native tickets, or headers are not delegated authority and must not be replayed to impersonate the original publisher. The configured retry service is explicitly authorized to finish accepted work; any application rule that also requires live reauthorization of the original producer must be satisfied before execution. Verify that this service may reply to the recorded original recipient under current policy; it cannot substitute an arbitrary destination. Never trust a self-asserted OriginalProducer header.

Requested operation scope is untrusted input to the existing IOperationScopeResolver. RM-03 then authorizes against the actual resolved resource and captured server identity. Apply registered capability checks to non-operation handlers as well. Authorize reply destinations against the caller; do not forward a sensitive result to an arbitrary supplied destination. [C3]

Keep credentials and payloads out of normal logs, metrics, health responses, and exception summaries. Do not add automatic payload logging or an external identity provider to this module. The fixture may use isolated development broker credentials; production provisioning and credentials remain application/deployment owned.

## 9. Bounds, retention, and lifecycle

### 9.1 Initial defaults

These are specification defaults, not measured performance claims. Validate them together and document the estimated buffering implied by configured payload limits and concurrency.

| Setting | Initial value / rule |
| --- | --- |
| Encoded payload | 256 KiB default; configurable up to 1 MiB and within the broker limit. |
| Envelope metadata | 16 KiB maximum; at most 32 bounded user headers, at most 8 KiB combined. |
| Publisher reservation batch / parallel publishes | 32 rows / 4 publications; materialize bodies only within the byte budget. |
| Broker publication deadline / reservation duration | 10 seconds / 30 seconds; conditional ownership updates, with bounded renewal if needed. |
| Consumer concurrency / prefetch | 8 / 16 per subscription, additionally constrained by a process-wide work and byte budget. |
| Process-wide admitted consumer work | 32 concurrent deliveries by default. |
| Messaging-owned encoded buffer budget | 32 MiB default, excluding documented native-client/runtime overhead. Admission blocks or rejects before exceeding owned bounds. |
| Scheduled handler retry budget | 5 ordinals total, including the initial ordinal; crash recovery may revisit an ordinal without repeating a protected committed effect. |
| Retry delays | 1 second, 5 seconds, 30 seconds, 2 minutes, with bounded jitter. |
| Outbox transient publication backoff | Exponential with jitter, capped at 60 seconds; durable pending work has no silent expiry. |
| Local RPC wait / maximum allowed wait | 30 seconds / 5 minutes. Both are finite and configurable within documented limits. |
| Outstanding process-local RPC waits | 256; reject excess before new durable submission. |
| Outbox idle poll interval | 1 second with a wake signal where available; no busy loop or per-poll success log. |
| Retained history and deduplication | No implicit deletion. Finite policies require an explicit supported retry/redrive horizon. |

Use one validated options snapshot for topology, identity, limits, codecs, and route definitions. Restart is required for changes that invalidate active resources; do not rebuild consumers on every configuration event. Expected retry jitter and time calculations use appropriate monotonic/database clocks; wall-clock changes must not extend local waits unexpectedly.

### 9.2 Retention

Keep Pending, Reserved, Blocked, unresolved, and scheduled-retry responsibility until resolved. Successful publication alone must not trigger immediate deletion of history when a configured retention period requires it.

Define separate policies for published payload history, successful inbox protection, terminal records, reply endpoint lifetime, and administrative redrive eligibility. Retain any provenance and reply references while dependent work remains unresolved. A tombstone may retain identity/digest after payload removal.

Finite deduplication retention must cover the declared broker retention, retry, and supported administrative replay horizon. Whole-record deletion ends that duplicate guarantee. Reject or explicitly route a requested redrive outside the supported horizon rather than advertising arbitrary historical duplicate safety. Keep existing RM-03 retention and privileged maintenance independent. [R4, C3]

Maintenance runs through explicit bounded APIs, or an explicitly selected existing hosting schedule. It is not enabled just by registering the module. No cleanup may delete pending responsibility to satisfy a storage cap. When configured storage headroom is exhausted, reject new staging or pause consumption with a defined health state while preserving already accepted durable work.

### 9.3 Startup and shutdown

Compose through existing CISApplication/hosting stages. Validate registrations without I/O; start only through the established lifecycle; verify schema/topology and establish consumers before declaring required messaging capabilities ready. Readiness reflects configured responsibility: an ingress that only stages durable work may remain ready during broker downtime while a request-serving deployment may require a live broker.

During shutdown, stop new caller/consumer admission, cancel local waits as specified, allow or cancel active handlers within the configured drain policy, await transaction and scope settlement, then stop publishers and dispose broker resources. Keep data sources and metrics callback owners alive until their dependants settle. Never acknowledge incomplete work merely to finish shutdown. Persisted responsibility and broker-unacknowledged deliveries must be recoverable on restart.

## 10. Automatic instrumentation and testing support

Use the existing CISLIB Metrics abstractions, exporter, central application/environment labels, and catalog limits. Instrument the owning boundary once. Use registered route/subscription aliases and finite outcomes; never use MessageId, operation keys, scope/tenant IDs, reply endpoint instances, payloads, exception text, or raw broker addresses as metric dimensions.

The following is the initial metric catalog. Names are logical CIS names; record actual exported names through the existing exporter convention during implementation. All histograms use nonnegative finite seconds and one shared bounded bucket profile selected within the existing catalog budget. Counter lifetime is the module/provider lifetime; no periodic resets.

| Logical instrument | Kind / boundary | Bounded dimensions and purpose |
| --- | --- | --- |
| cis.messaging.publish.duration | Seconds histogram from one admitted native publication attempt to its actual settlement. | Registered route and confirmed/rejected/unknown/cancelled; count supplies attempt volume. |
| cis.messaging.delivery.duration | Seconds histogram for one admitted consumer attempt through transaction/disposition settlement. | Subscription and handled/duplicate/retry/terminal/unknown/cancelled; diagnose processing time. |
| cis.messaging.rpc.duration | Seconds histogram from admitted RequestAsync entry to final owned settlement. | Registered request alias and finite exchange outcome; observe caller latency. |
| cis.messaging.active | Up/down counter for admitted work, balanced in every exit path. | publish/consume/rpc kind; observe current load. |
| cis.messaging.dispositions | Counter at a known committed retry, terminal, or duplicate-suppression disposition. | Subscription and fixed disposition; distinguish repeated delivery from business rejection. |
| cis.messaging.outbox.pending | Gauge of the last bounded store observation, not a scrape-time query. | Registered database/store alias; expose pending responsibility. |
| cis.messaging.outbox.oldest.age | Seconds gauge from the last valid store observation. | Store alias; detect delivery backlog. |

When a store observation is unavailable/stale, omit it or expose its defined validity state; do not publish zero as if the backlog were empty. Logical staging, actual publication attempts, and business completion are different measurements. Log expected steady-state success only at explicitly selected diagnostic levels. Bound repeated outages/failures and recovery logs, including when a Heartbeat callback polls frequently.

Provide public, bounded test doubles in CIS.CommonTesting for publisher outcomes, delivery contexts, and typed reply waiting. They support deterministic unit tests and do not claim SQL atomicity, broker persistence, authentication, or network-fault proof. Reuse existing native MSTest and provider/package fixtures. Do not build a custom harness, mutation campaign, performance campaign, or large evidence archive for this feature.

## 11. Focused acceptance criteria

These are behaviour scenarios, not a mandated one-test-per-row structure. Use small parameterized tests only where each case covers a distinct failure boundary. Mark scripted and real-provider evidence separately. Run required current repository gates; broaden testing only for an identified remaining risk.

### 11.1 Part A

| ID | Scenario | Required result |
| --- | --- | --- |
| A01 | Commit and rollback application changes plus outgoing record. | Both commit together or neither does; no publication before commit; staging receipt is not committed success. |
| A02 | Competing publishers and an expired reservation with a delayed former owner. | Conditional updates reject stale ownership; repeats preserve MessageId; accepted work cannot disappear. |
| A03 | Broker acceptance with lost confirmation; uncertain Published-state commit; unroutable command. | Recovery preserves identity/bytes; Unknown is not Confirmed; unroutable work is retained with its defined disposition. |
| A04 | Failure before commit, after local writes before commit, and after commit before acknowledgement. | Rolled-back work can retry; committed effects do not repeat; Unknown uses authoritative resolution/arbitration. |
| A05 | Two independent subscribers, each with competing instances and restart. | Each logical subscriber can commit its effect; instances share duplicate protection; identity survives restart. |
| A06 | Same business operation in new envelopes; same MessageId with changed bytes; same operation key with changed business input. | Matching operation recovers original result; transport-content conflict and operation conflict remain distinct and never overwrite results. |
| A07 | Transient failure, business rejection, unsupported schema, and exhausted scheduled retries. | Finite ordinal budget/delays apply; crash reinvocation of one ordinal is distinguished from a newly scheduled retry; business rejection completes; terminal failures remain inspectable. |
| A08 | Crash during retry/terminal disposition, after its commit before source acknowledgement, during target publication, and a duplicate success in the post-rollback gap. | No lost responsibility or overwritten success; no unbounded duplicate retry chains; retry targets only the failed subscription. |
| A09 | Restart with unpublished committed rows and unacknowledged broker deliveries. | Recovery occurs; business consumers never poll/execute outbox rows directly. |
| A10 | Retention boundary, pending cleanup, and explicit redrive. | Pending responsibility/provenance survives; tombstones protect supported replay; beyond-horizon handling is explicit. |
| A11 | Capacity exhaustion and shutdown with active work. | Work/byte/wait bounds hold; admission failure is explicit; actual cleanup settles and unfinished work is not falsely acknowledged. |
| A12 | Forged/missing producer evidence, prohibited reply route, revoked permission, conflicting content, internal retry identity, and malformed/oversize input. | No unauthorized handler/result disclosure or poisoned legitimate inbox record; retries use current service authority without impersonating stored callers. Verify bounded quarantine and test with another real broker principal. |

### 11.2 Part B

| ID | Scenario | Required result |
| --- | --- | --- |
| B01 | Typed query and durable command, including business rejection and fast response. | Correct response/correlation; waiter registration cannot miss an immediate reply; broker acknowledgement is not business success. |
| B02 | Timeout/cancellation after remote commit, then late/duplicate reply. | Defined wait outcome with correct submission fact; no remote rollback claim, waiter leak, or second completion. |
| B03 | New request attempt retries a committed operation. | Original result returned to current authorized correlation/reply route; no second committed effect. Include conflicting input, denied replay, and expired/incompatible result. |
| B04 | Exact request envelope repeats; two caller instances are waiting; an unauthorized or mismatched reply is injected. | Reply responsibility remains secured; no other instance consumes/discards the active caller's reply; duplicates are harmless and rejected replies cannot complete a legitimate wait. |
| B05 | Responder crashes after effect/result/reply-outbox commit; caller restarts before response. | Reply publisher recovers; new attempt retrieves original result; old endpoint retirement cannot reexecute the operation. |
| B06 | Wait requested before staged-request commit; unknown submission; overload. | Unsupported wait rejected promptly; Unknown stays distinct; overload does not create an untracked durable request. |

For both parts, include one actual exported-metrics check with central identity and bounded labels, and one public-package consumer check that Core-only consumers acquire no RabbitMQ/Npgsql dependency. Reuse existing gates and fixtures rather than creating separate campaigns for these cross-cutting requirements.

Part A's acceptance report must identify the tested database/broker/client versions and platform(s). A single-node broker test establishes restart/persistence behaviour for that setup, not quorum failover. Only claim multi-node failure handling if the corresponding case was actually executed. Report unexecuted deployment requirements plainly.

## 12. Implementation order and deliverables

Use the existing CIS requirements/import and bounded-planning workflow if active in the target repository. Preserve this specification as one feature input. Reuse existing authorization, accepted decisions, and valid evidence; do not reopen completed historical owners. Documentation, focused tests, review, and closeout remain within coherent implementation work. [C0]

| Work item | Part | Outcome and dependency |
| --- | --- | --- |
| A1 Shared contract and store | A | Envelope, codecs, result semantics, explicit outbox/inbox APIs, PostgreSQL schema and atomicity. |
| A2 Broker integration and publication | A | Optional RabbitMQ adapter, identity evidence, topology profile, confirmed publisher and restart recovery. Can proceed alongside store details after shared contracts settle. |
| A3 Consumption and disposition | A | DI/security dispatch, uniqueness arbitration, RM-03 binding, durable retry/terminal handoff. Depends on A1/A2 interfaces. |
| A4 Composition and Part A acceptance | A | Hosting, bounds, instrumentation, CommonTesting, docs/example and focused real-provider evidence. |
| B1 Request/reply client and responder | B | Query/durable command registration, tickets/waits, reply routing and typed outcomes on Part A infrastructure. |
| B2 Recovery and Part B acceptance | B | Current-attempt result recovery, restart/late replies, endpoint retirement, focused evidence and complete consumer guidance. |

Parallelize only with explicit file ownership and stable shared interfaces. One owner coordinates project/package files and builds sharing output paths. Do not create a separate plan/spec per part or a separate framework for RPC.

Required implementation deliverables:

- One maintained portable messaging contract under contracts/messaging/.
- Core runtime/store, optional RabbitMQ integration, and explicit schema/topology setup assets.
- Usage and ownership guides under docs/modules/messaging/, including the two parts, failure outcomes, recovery, provider limits, and declared retention horizon.
- Bounded CIS.CommonTesting doubles and focused native/provider/package evidence.
- An opt-in CountryData messaging example or focused existing fixture composition. Keep CountryData's database-free default and its status as the one maintained tutorial; do not add another sample application. [C0]
- A repository consumer skill at .github/skills/cislib-messaging/SKILL.md and a concise AGENTS.md routing section outside managed CIS markers. Link actual implemented APIs, contract, example, provider setup, and tests.

The messaging skill must teach explicit transaction staging, the difference between staged/committed/confirmed/processed, one RM-03 attempt per context, safe repeats, trusted scope and reply authorization, bounded retries, request timeout uncertainty, result recovery, lifecycle, metrics, and evidence limits. Validate with the currently installed repository skill tooling. This deliverable is repository guidance, not an instruction to install a new personal assistant skill.

### 12.1 Embedded implementation handoff

Implement this specification in its two ordered parts against current CISLIB main. Inspect current instructions, dependency policy, Transactions, Operations, Security, Metrics, hosting and native test conventions first. Preserve existing public contracts and the single CIS.Core base assembly. Keep RabbitMQ optional and the native database provider application owned.

Establish shared contracts, then implement Part A's real transactional publication and safe consumption. Implement Part B using the same outbox, dispatcher, security, retry, and operation-result machinery. Do not add a second idempotency coordinator or persist caller Tasks. Complete focused acceptance, examples, documentation, CommonTesting, and the repository messaging skill within the implementation work.

Report exact behaviour delivered for each part, meaningful executed evidence, provider/deployment assumptions, and any remaining acceptance gaps. Do not claim broker delivery from an outbox commit, business success from a broker confirmation, or rollback from a timeout. Do not publish a release, merge main, or deploy infrastructure merely because this specification exists.

## 13. Sources and authoritative references

Repository references are pinned to the reviewed baseline. The implementation must recheck current accepted contracts before editing.

- **C0:** [CISLIB baseline commit](https://github.com/andrewspiteri/CISLIB/commit/669d286711340e7f63398256cee27575932f58f4) and [agent guidance](https://github.com/andrewspiteri/CISLIB/blob/669d286711340e7f63398256cee27575932f58f4/AGENTS.md).
- **C1:** [Approved C# dependencies](https://github.com/andrewspiteri/CISLIB/blob/669d286711340e7f63398256cee27575932f58f4/docs/architecture/csharp-dependency-policy.md).
- **C2:** [Transactions contract](https://github.com/andrewspiteri/CISLIB/blob/669d286711340e7f63398256cee27575932f58f4/contracts/transactions/README.md).
- **C3:** [Operations contract](https://github.com/andrewspiteri/CISLIB/blob/669d286711340e7f63398256cee27575932f58f4/contracts/operations/README.md).
- **C4:** [PostgreSQL operation journal](https://github.com/andrewspiteri/CISLIB/blob/669d286711340e7f63398256cee27575932f58f4/docs/modules/operations/postgresql.md).
- **C5:** [Existing trusted host-context bridge](https://github.com/andrewspiteri/CISLIB/blob/669d286711340e7f63398256cee27575932f58f4/implementations/csharp/src/CIS.Core/Security/Hosting/SecurityHostContextScope.cs).
- **R1:** [RabbitMQ acknowledgements and publisher confirms](https://www.rabbitmq.com/docs/confirms).
- **R2:** [AWS transactional outbox guidance](https://docs.aws.amazon.com/prescriptive-guidance/latest/cloud-design-patterns/transactional-outbox.html).
- **R3:** [RabbitMQ reliability guide](https://www.rabbitmq.com/docs/reliability).
- **R4:** [Microsoft idempotent consumer pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/idempotent-consumer).
- **R5:** [RabbitMQ quorum queues and safe dead lettering](https://www.rabbitmq.com/docs/quorum-queues).
- **R6:** [RabbitMQ queue ordering](https://www.rabbitmq.com/docs/queues).
- **R7:** [Official RabbitMQ .NET client](https://www.rabbitmq.com/client-libraries/dotnet) and [client API guide](https://www.rabbitmq.com/client-libraries/dotnet-api-guide).
- **R8:** [RabbitMQ Direct Reply-To semantics](https://www.rabbitmq.com/docs/direct-reply-to).
- **R9:** [RabbitMQ validated user-id](https://www.rabbitmq.com/docs/validated-user-id) and [access control](https://www.rabbitmq.com/docs/access-control).

The common requirements, defaults, API proposals, two-part plan, and acceptance criteria are the CISLIB design specified here. External broker documentation supports the cited provider behaviour; it does not establish that this implementation or its deployment has already passed acceptance.

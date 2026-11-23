---
title: Audit contracts and delivery
type: document
status: Draft
---

# Audit contracts and delivery

`CIS.Audit` lives in the existing Core package. It captures attributable immutable
facts and publishes them through Messaging. Applications select which business
actions require Audit; an authorization observation cannot infer that an action
completed or that a transaction committed.

The [portable v1 contract](../../../contracts/audit/README.md) defines the closed
common envelope and its exact byte comparison rules. The existing Messaging
contract is `cis.audit.record`, schema version 1, Event. Application event names
and versions belong inside that one payload and do not create a route per action.

## Select definitions and hosting

Register the definition before the existing Messaging graph freezes:

```csharp
services.ConfigureCisAudit(new AuditOptions("audit"));
// Complete the application's existing Messaging options, store and transport,
// and its Security registration. No Audit destination backend is selected.
services.AddCisMessaging();
services.AddCisAudit();
// Follow with the existing Workflow/REST/Coordination/Heartbeat/Startup bridges.
```

Use `CIS.Audit.Hosting` for these extensions. Definition/capture registration
performs no database, broker, topology or worker operation. It registers no
production writer. The selected delivery bridge requires the actual matching
Messaging registration, route and limits; it refuses an existing explicit testing
writer instead of claiming ownership of it. Contract-only and application unit
consumers can use the definition with an explicitly registered testing writer.

In `CISApplication`, call `builder.UseAudit(new AuditOptions("audit"))` alongside
the existing `UseMessaging` and explicit store/transport setup. The facade uses
the same provider and arranges the cooperating bridges. Native startup remains
sequential: credentials and application preparation, Messaging, Audit, then
Coordination/jobs. Audit opens its logical publication gate just before Messaging
starts consumers, so their authorization hooks can publish observations during
that native startup. This starts no Audit worker or I/O operation.

On shutdown, admitted jobs retain access for their final Audit calls. Their
actual drain precedes Audit capture/publication drain, which precedes stopping
Messaging and the cooperating dependencies. An ignored cancellation cannot
complete an active mapper or native publication. Failed facade Build also retains
and joins exactly the capture/writer owners already constructed, including when
the final Audit runtime was never constructed. Applications still own data
sources and other externally supplied instances.

## Capture before publication

Use `IAuditCapture` in the actual caller scope. A typed `AuditEventDefinition<T>`
selects the event name/version/category and an explicit `IAuditDetailsCodec<T>`.
That codec validates the application's selected details schema and writes the
JSON object with `Utf8JsonWriter`; it does not reflect arbitrary entity graphs.
Event definitions can require a target, reason or original operation reference.
Amounts requiring decimal precision use a documented lossless decimal string,
never a binary floating-point round trip. Select safe fields deliberately; omit
credentials and unfiltered request or response bodies.

```csharp
CapturedAuditRecord captured = capture.Capture(definition, details,
    new AuditCaptureOptions(outcome: AuditOutcome.Succeeded,
        target: new AuditTarget("ledger", "production", "account", accountId)));
```

Producer identity comes from the existing local identity. Actor facts come from
the trusted caller accessor, including issuer-qualified subjects and established
human delegation. A missing caller is `unknown`; it is not automatically
anonymous. `CaptureLocalSystem` explicitly selects a local system action.
Local process instance metadata is `reported`, never verified replica identity.
Capture does no persistence or network I/O and is not durable acceptance.

The capture detaches mutable details, takes the ID and recorded timestamp once,
and preserves the final complete bytes. A missing occurrence time equals that
same recorded instant. Public byte access returns a defensive copy. Reusing the
capture does not recapture its actor, ID, time or details.

`AuditRecord` is portable data. `CapturedAuditRecord` is source eligibility from
trusted local capture. Decoding external bytes, including bytes claiming the
local producer, does not create a captured source record or authenticate anyone.

## Original transaction and independent observations

```csharp
AuditStageResult staged = await writer.StageAsync(owner.Context, captured, token);
// The application still owns completion, after all original-context work settles.
TransactionCompletion completion = await owner.CommitAsync(token);
```

`StageAsync` uses `IMessageOutbox.PublishAsync` on exactly the supplied participant.
It does not substitute, retain, commit or dispose the owner. Existing configured
database, active/non-poisoned and ReadCommitted restrictions remain. `Staged`
proves only an outgoing record in that uncommitted unit; inspect the owner's real
completion separately. A business change and required outgoing record commit
together only when both use this original supported transaction.

Audit-specific refusal or failure within staging marks a usable original context
rollback-only, including failure before entering Messaging. Preserve an original
fault if marking a disposed context also fails. Capture occurs before the writer:
if capture fails after business work began, the application must explicitly mark
rollback-only or abandon/dispose that owner. Swallowing a capture exception and
then committing is invalid usage.

For an independent denial, confirmed failure after rollback or post-effect hook:

```csharp
AuditSubmissionResult result = await writer.SubmitAsync(captured, token);
```

Submission delegates the short transaction to `IMessagingClient.PublishAsync`.
`OutboxCommitted` is known source outgoing commitment; `NotSubmitted` is known
noncommit; `Unknown` means commit may have occurred. The result retains the exact
Messaging receipt and actual transaction completion/cleanup where available.
Never automatically repeat an Unknown submission or its business action.

AuditId identifies an immutable occurrence. Each successfully staged or committed
publication has new Messaging MessageId/OutboxRecordId values. Two successful
submissions of one capture are two transport envelopes; a refused call may have
no transport receipt. Existing transport retries retain their original IDs and
exact payload bytes. This module supplies no durable AuditId deduplication.

## Receiving and persistence boundary

Use `IMessageHandler<AuditRecord>` and its original
`MessageHandlerContext.Transaction`. Authenticate and authorize through existing
Security/Messaging. Bind `record.Producer.Application` and `.Environment` to the
authenticated `context.Envelope` producer. A fixed trusted ingress target works
for both initial and retry authorization; the current retry service caller is
not the original producer. A configured RetryValidator can additionally validate
retained original MessageProvenance. Payload actor facts grant no authority.

The generic codec rejects unsupported envelope versions, unknown common fields,
duplicate keys at every depth, malformed UTF-8/Unicode, comments, trailing input
and invalid required values. It preserves structurally valid unknown application
events/details and exact original bytes without fetching schemas or requiring a
global event registry. A domain receiver can interpret the schemas it supports.

A future durable destination must preserve the original record or an equivalent
durable handoff, detect identical AuditId/identical original payload as repeated,
and reject conflicting bytes without replacing history. Outer transport metadata
does not participate in that comparison. Secure acceptance and inbox disposition
atomically where they share the original transaction, and acknowledge only known
committed responsibility. Keep unresolved responsibility on the existing failure
path. This module implements no destination store or persistence enforcement.

Staged, OutboxCommitted, broker Confirmed and receiver Processed are separate
milestones. None proves a later one or archival storage, retention, protected media
or compliance. Corrections are new records; do not rewrite an old occurrence.

## Existing Security hooks

Select `AddCisSecurityAuditBridge(new AuditSecurityBridgeOptions(...))` explicitly
before `AddCisSecurity`. It rejects registration when an existing unkeyed sink is
present; it does not implicitly compose or replace that sink. The singleton bridge maps the detached immutable observations and
uses the local identity and clock; it never captures a scoped caller accessor.

`cis.security.authorization.decision` v1 carries the complete final decision and
capabilities, with missing caller/target facts intact. `cis.security.operation.reported`
v1 carries the owner's reported post-effect outcome, without inventing an
operation ID, occurrence timestamp or committed business transaction. A process
failure between the effect and hook remains a capture gap. See the existing
[Security audit guide](../security/audit.md) and the two portable detail schemas.

The positive cooperative submission deadline defaults to five seconds and may
be selected through one minute. Earlier caller cancellation wins; capture and
actual publication/provider cleanup still settle before return. An established
OutboxCommitted result survives late cancellation. Known refusal or Unknown
throws `AuditSecurityBridgeException` with its exact public result. Original
programming faults and cancellation propagate unchanged.

Configure narrow `AuditSecurityIngressBinding` entries only for dedicated trusted
Audit ingress resources and their complete initial/retry capability sets. This
omits recursive automatic authorization observations, while actual authentication,
authorization and denial still run. It never suppresses explicit operation
reports or unrelated business authorization. Do not use a payload/header switch,
reuse those infrastructure targets for business work, or disable Security globally.

## Bounds and testing

Complete Audit payloads default to 256 KiB and JSON depth 16, selectable from a
positive value through 1 MiB/depth 32 and constrained by actual Messaging limits.
Security identity values retain their existing Unicode-scalar bounds without
truncation. Correlation stays complete in Audit; its optional outer diagnostic
copy is omitted when it cannot fit Messaging's UTF-8 byte bound.

Capture reserves finite encoding capacity nonblockingly from the existing
Messaging process budget before allocating its additional library-owned buffers.
It does not wait on capacity held by an outer Messaging operation or increase
that capacity. Returned captures are application-owned objects; this is not a
global application heap limit. Library shutdown closes admission and joins actual
accepted capture/publication work, including application codecs that have not
returned. Cancellation requests do not manufacture completed cleanup.

Use `ScriptedAuditWriter` from `CIS.CommonTesting` for bounded consumer tests of
the public writer port. It records safe immutable metadata and returns explicit
configured facts, without encoding, poisoning or owning transactions. Scripts
prove no SQL atomicity, authentication, broker delivery or destination storage.
Native acceptance uses the existing isolated PostgreSQL/RabbitMQ fixtures;
portable vectors describe receiver obligations without implementing an archive.

When the existing Core module collector is selected, the only new stream is
[`cis.audit.capture.results`](../../../contracts/metrics/audit.md), with category
and capture outcome: nine possible combinations and central application/environment
identity. It counts capture, not staging or delivery. Messaging retains backlog,
age, retry and failure observations. Routine captures are quiet; failure warnings
share a finite lifetime budget across flaps. No record payload, subject, AuditId,
resource or correlation value enters those labels or routine logs.

The opt-in [CountryData observation](../../../implementations/csharp/examples/CountryData/README.md)
and its existing example unit project use public capture and CommonTesting. The
default tutorial remains database-free. Real transaction and delivery examples
live in the existing provider/package test infrastructure, with a test-only
receiving probe and no production archive.

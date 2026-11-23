---
title: Security authorization and operation audit boundaries
type: document
status: draft
cis:
  stable_id: cislib.document.modules.security.audit
---

# Security audit boundaries

`ISecurityAuditSink` has two awaited integration hooks. Register a sink before
`AddCisSecurity`; otherwise Core supplies its existing no-op. The hook itself
provides no persistence receipt. Applications can explicitly select the RM-06
Audit bridge to await committed outgoing responsibility through existing Messaging.

`AuthorizationCompletedAsync(SecurityAuthorizationAudit, token)` observes the
final composite native decision, including additional policy requirements. It
contains the actual established caller when available, complete capability set,
resolved target and bounded correlation. Failed authentication invents neither
caller nor resource. Passing one requirement is not a successful composite result.

`OperationCompletedAsync(SecurityOperationAudit, token)` is separate. The business
owner supplies its actual completed, failed or canceled outcome. Authorization
success alone proves neither mutation commit, delivery nor downstream completion.
A denied request produces no fictional business-completion record.

CountryData authorizes its actual cached country, commits its single selection/
revision, then awaits the operation sink with the actual caller and target. A
hook fault remains observable while the committed selection remains committed.
There is no implicit rollback or successful-delivery claim. Business effects that
must be atomic with audit intent use `IAuditWriter.StageAsync` with the original
transaction participant at their actual business boundary. The automatic bridge
uses standalone submission and cannot add atomicity to an already completed effect.

## Selecting the Audit bridge

Use `CIS.Audit` and `CIS.Audit.Hosting`. Define the Audit route with
`ConfigureCisAudit(new AuditOptions("audit"))`, select the complete existing
Messaging host, and select its native writer with `AddCisAudit()`. Register the
bridge before Security:

```csharp
services.AddCisSecurityAuditBridge(new AuditSecurityBridgeOptions());
services.AddCisSecurity(securityOptions);
```

Registration performs no I/O and resolves no existing sink. An existing unkeyed
`ISecurityAuditSink`, including Security's already installed no-op, is rejected
instead of silently replaced. Keep one selected unkeyed sink. The concrete
`AuditSecurityBridge` is also registered for explicit application composition.
An application that supplies a testing writer needs only the passive Audit
definitions and capture registration, without selecting the native writer.

The bridge is a singleton and consumes each hook's immutable trusted observation.
It retains no scoped caller accessor. Producer identity comes from central local
identity; actor identity comes from the supplied trusted caller. A missing caller
remains unknown. The bridge never uses credential validation time as occurrence
time. Each observation receives a fresh AuditId, with one capture time used for
both occurrence and recording when the hook has no separate occurrence timestamp.

`SecurityAuditEvents.AuthorizationDecision` selects
`cis.security.authorization.decision` version 1. Its top-level outcome is allowed
or denied, and its required reason preserves the decision code and available safe
failure message. Target and caller can be absent after failed authentication.
Details preserve all one through 256 requested capabilities in their original
order, including duplicates, plus the decision code and available safe failure
source, HTTP status, provider code and transport code.

`SecurityAuditEvents.OperationReported` selects `cis.security.operation.reported`
version 1. It preserves the actual target and the owner's completed, failed or
canceled report, with optional safe outcome code. It does not infer an Operations
identity, causation identity or transaction rollback from an old hook's correlation
or `*.unconfirmed` code. Security correlation remains complete in the Audit payload;
the outer Messaging correlation is omitted when it exceeds that transport's
128 UTF-8 byte bound.

The separate producer detail schemas are
[authorization](../../../contracts/audit/v1/security-authorization-details.schema.json)
and [operation](../../../contracts/audit/v1/security-operation-details.schema.json).
The common receiving decoder still preserves unknown application event versions
and opaque details; decoding establishes neither source eligibility nor authority.

## Settlement and failure policy

`AuditSecurityBridgeOptions.SubmissionTimeout` defaults to five seconds and must
be positive and no greater than one minute. The bridge links this cooperative
deadline to the caller's cancellation, then awaits the actual writer operation
and its owned cleanup. Cancellation does not detach work or prove rollback. A
known committed result remains committed if cancellation arrives during settlement.

Normal return from a nonsuppressed bridge call establishes only
`AuditSubmissionOutcome.OutboxCommitted`: outgoing responsibility is committed,
with no broker confirmation, receiver persistence or business-success claim.
Optional actual transaction completion is retained when available.
`AuditSecurityBridgeException.Result` preserves the exact NotSubmitted or Unknown
result, including available transport receipt, safe error and actual cleanup facts.
A capture capacity or closing refusal is known NotSubmitted with its allocated
AuditId and no invented receipt. Original programming faults and cancellation
remain observable. Null or mismatched writer results are programming faults.
There is no automatic retry, and an Unknown result must not replay business work.

Authorization hooks are awaited before protected work proceeds. Applications must
choose how an observable bridge failure affects their request. A post-effect
operation hook failure cannot undo an already committed effect. Messaging
administration and CountryData preserve their existing post-effect boundaries.

## Dedicated receiver recursion boundary

An Audit receiver can configure up to sixteen immutable
`AuditSecurityIngressBinding` values, each with a complete dedicated resource
target and one through eight distinct initial capabilities, plus an optional
complete retry set. Resolve this exact target from the trusted subscription
registration, for example by closing over an `audit.subscription` resource and
subscription alias. Never select it from a payload, header or caller-controlled
suppression flag.

The bridge omits only automatic authorization observations whose entire target
and entire capability set match one configured binding. Reordering is immaterial;
missing, extra or duplicate capabilities do not match. Native authentication,
current capability authorization and denials still run. Null-target authentication
failures outside that delivery boundary remain auditable. Existing Messaging
authentication failures occur before its authorization hook and generate no
recursive bridge observation. Source outbox publication has no capability hook.
Explicit operation reports always remain auditable.

Reserve these targets and complete capability sets for Audit infrastructure; do
not reuse them for business authorization. This narrow omission does not suppress
other delivery, retry, administration or business security observations. The receiver
also binds the payload producer to `MessageHandlerContext.Envelope` producer facts
after native Messaging provenance validation. Retry validation preserves the
original producer rather than replacing it with the current retry service.

Preserve hook faults and cancellation settlement. The authorization hook completes
before protected work proceeds; the operation hook runs at its real business
boundary. Records omit raw tokens, keys, arbitrary claims and HTTP-context
retention. Constructing, serializing or retaining a record grants no background
request authority. Reported instance metadata remains unverified unless a real
integration established stronger evidence.

Metrics are observations, not audit receipts; aliases/outcomes are finite and
raw subject/URI/correlation histories are excluded. The native Windows custody
journal records only exact newly owned backing metadata for cleanup/restoration,
not security decisions. See [transport](transport.md), [consumer](consumer.md)
and [failures](failures.md).

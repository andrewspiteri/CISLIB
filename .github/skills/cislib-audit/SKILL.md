---
name: cislib-audit
description: Implement, use or review CISLIB Audit capture, original-transaction staging, standalone submission and the explicit Security bridge over existing Messaging. Apply to CIS.Audit contracts and consumers; destination archive storage remains separate.
---

# CISLIB Audit

Read [repository guidance](../../../AGENTS.md), the
[portable contract](../../../contracts/audit/README.md) and the single
[consumer guide](../../../docs/modules/audit/README.md). For the existing hooks
read [Security audit](../../../docs/modules/security/audit.md); for native
ownership use [Messaging publication](../../../docs/modules/messaging/publication.md),
[receiving](../../../docs/modules/messaging/dispatch.md) and
[hosting](../../../docs/modules/messaging/hosting.md).

- Capture through the trusted scoped `IAuditCapture` and an explicit typed event
  definition/details codec. Select safe fields, retain lossless decimal strings
  where required, and detach inputs. Local identity is the producer; the caller
  is a separate actor. Missing identity stays unknown. Reported process metadata
  does not become verified instance or human evidence.
- `AuditRecord` is portable data; only trusted local capture creates
  `CapturedAuditRecord`. Decoding, matching producer strings or retaining actor
  fields grants no source eligibility or receiving authority. Generic forwarding
  retains exact original bytes and structurally valid unknown application details.
- Stage through `IAuditWriter.StageAsync` on the original RM-02 context and
  await it before completing that owner. Audit-side staging refusal/fault poisons
  a usable context. Capture failure before staging requires the application to
  poison or abandon that owner. The Audit writer never substitutes, retains or
  completes it; the application still owns its completion.
- Standalone submission delegates its short transaction to Messaging. Preserve
  NotSubmitted, OutboxCommitted and Unknown, exact receipts and actual completion/
  cleanup. Do not automatically repeat Unknown or the business action. Staging,
  source commit, broker confirmation and receiver processing prove different facts.
- Capture ID/time once. Deliberate re-submission of one capture creates another
  MessageId; transport retry retains its original MessageId and exact bytes.
  Same-AuditId identical/conflicting-byte enforcement belongs to a future durable
  receiver. Add no Audit store/provider, archive, extra inbox/outbox, spool, retry
  scheduler, acknowledgement exchange or idempotency coordinator.
- Select the Security bridge explicitly before AddCisSecurity without silently
  replacing a real sink. Keep complete capabilities and detached trusted hook
  facts. Reported post-effect completion is not transaction evidence and retains
  its capture gap. Cooperative deadlines still await actual cleanup; preserve
  late established commit, original faults and cancellation.
- Suppress only automatic authorization observations for the configured dedicated
  ingress target and complete ingress/retry capability sets. Authentication,
  authorization and denial continue. No payload/header bypass or global switch.
  Bind received Audit producer to the authenticated MessageHandlerContext.Envelope
  producer; retries retain original provenance rather than impersonating their
  current retry caller. Stored actor facts grant no authority.
- Configure the route before Messaging hosting freezes. Use the existing provider
  and sequential startup. During stop, jobs drain first, then actual Audit work,
  then Messaging and dependent modules. Constructors and definition registration
  perform no I/O. Retain exact partially constructed ownership on failed Build.
- Reuse the existing process byte admission before additional encoded copies,
  finite payload/depth limits and bounded quiet diagnostics. Application-owned
  captures are not a global heap bound. The sole optional capture counter has
  nine combinations; reuse Messaging delivery health/metrics and omit payload,
  subjects, IDs, resources and correlation from labels and routine logs.

Use `ScriptedAuditWriter` for bounded public consumer facts, never as SQL, native
authentication, broker or archive proof. Reuse existing PostgreSQL/RabbitMQ,
public-package and schema fixtures for the distinct native boundaries. Keep
[CountryData](../../../implementations/csharp/examples/CountryData/README.md)
opt-in and database-free by default, with CommonTesting examples in its existing
unit project. Report actual executions and their limits; source alone is not proof.

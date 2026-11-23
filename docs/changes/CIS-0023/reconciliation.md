---
title: RM-06 contract reconciliation
type: document
status: Draft
change_id: CIS-0023
---

# RM-06 contract reconciliation

The supplied spec was inspected at `eee73d58`; this change starts at merged
`dcb61dc1`, including RM-05 and its two fixes. Existing Security audit records,
hook signatures and Messaging administration post-effect semantics are unchanged
across that range. Current coordination-aware host draining is an additional
integration obligation. No blocked design question currently requires a separate
product decision; routine API details remain inside proposed P01–P16.

| Existing contract | RM-06 treatment |
| --- | --- |
| Security hooks carry detached immutable observations; AddCisSecurity supplies a TryAdd singleton no-op only when unselected. | Preserve all signatures/constructors/defaults. Ordinary capture is scoped; selected bridge can be singleton without capturing ICallerContextAccessor. Explicit bridge registration refuses ambiguous real-sink replacement. |
| Security identities/correlation/outcome use scalar bounds; Messaging transport correlation uses128UTF-8bytes. | Preserve full valid Security data in Audit. Omit only an incompatible optional outer correlation; never truncate or misclassify identity. Full256capability observation is details, not the at-most8Messaging route capability list. |
| Public MessageOutbox staging uses original active ReadCommitted participant and internal nonblocking four-envelope byte reservation. | Add Audit-side poisoning/admission before delegation, preserve original context and underlying receipt. Reuse same-Core internal budget for additional encoding copies, not a new public memory framework. |
| Public MessageSubmissionClient event publication returns only NotSubmitted/OutboxCommitted/Unknown. | Preserve exact completion/receipt. The broader enum's NotConfirmed typed-ticket state is invalid here and must not be silently reclassified. |
| MessageWire has strict UTF8/duplicate-key validation; outer metadata has separate16KiB allowance. | Reuse strict primitives without relaxing RM04. Validate complete Audit body/depth against actual payload limits; codec retains exact original bytes and unknown details. |
| Generic receiving context has original transaction, envelope and services, but no retained original provenance property. TargetResolver runs for both initial/retry and sees only the current caller. | Resolve the fixed trusted ingress target, then bind Audit producer to the authenticated MessageHandlerContext.Envelope producer in the receiving helper/example. RetryValidator additionally checks retained original MessageProvenance. A local retry caller is not the original producer; no new context API or forwarding protocol is needed. |
| Messaging source publication does not invoke the capability authorizer. Native ingress rejects missing/failed caller evidence before the only automatic authorization hook. | No source authorization recursion. A trusted fixed Audit target and complete ingress/retry capability sets can suppress only the recursive automatic observation; HTTP null-target denials remain ordinary auditable facts. |
| RM-05 keeps job dependencies available through actual draining callbacks/scopes. Known host tails/facade explicitly identify cooperating owners. | Add Audit to those existing precise order/stop paths and failed-Build capture as needed. Keep final job capture available, join Audit before Messaging disposal, and preserve sequential startup. |
| Existing schema evaluator is a strict subset of draft2020-12 and lacks array count keywords. | Express valid Audit actor/instance alternatives with supported anyOf/localdefs/closed properties. Permit a small minItems/maxItems extension with focused regression for the256capability detail-schema limit, without a package/general evaluator campaign. Lexical UTF8/duplicate/depth checks remain runtime responsibilities. |
| Existing native PostgreSQL/RabbitMQ and public-package fixtures cover distinct foundation facts. | Add focused Audit cases in those structures. Runtime reconstruction satisfies AC07; no crash-process mode, collector or destination storage is needed. |

The two scoped source reviews are retained in the intake cost record. They are
design evidence, not native acceptance. Future receiving storage must still bind
current source authority, preserve original bytes, enforce AuditId duplicates/
conflicts across MessageIds and secure acceptance/inbox responsibility before ACK.
[RabbitMQ's acknowledgement guide](https://www.rabbitmq.com/docs/confirms)
confirms that publisher confirmations and consumer acknowledgements establish
separate boundaries; neither is an archive receipt.

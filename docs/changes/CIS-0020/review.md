---
title: "RM-04 consumption and readiness correction review"
type: document
status: Draft
---

The user supplied three required findings against `0a77508e`. Independent source
analysis confirmed all three. CIS-0020 owns this correction; CIS-0019's completed
history, costs and immutable candidate evidence remain unchanged.

The pressure fix acquires input and prospective response byte capacity in one
bounded operation before decoding. It uses the existing idempotent lease and
reply-ingress reservation. The original minimum-budget fixture allowed two total
delivery slots, leaving one ordinary slot; it therefore could not expose two
ordinary requests competing for response capacity. The new three-slot regression
holds the first request, observes the second waiting without a partial byte lease,
then requires both requests and a third delivery to finish. Cancellation coverage
joins actual native requeue and active-owner shutdown before reclaiming capacity.

The quarantine fix retains an original Reply as opaque Rejection evidence. Only
actual Reply purpose writes the business-reply correlation column. The existing
schema-v2 constraint remains correct and unchanged. Original tests did not follow
a well-formed Reply on ordinary ingress through production SQL validation and
quarantine publication. The new SQL/native regression requires exact retained wire
bytes, no canonical claim, actual quarantine confirmation, and a legitimate Event
with the same MessageId processed by the same still-registered consumer.

The readiness fix applies the captured option to the existing subscription/request
responsibility predicate. The original checks covered the default policy and stop,
not both option values after broker disconnection. New unit rows cover staging,
ordinary subscription, typed caller and responder responsibilities and preserve
startup/store/stop gates. The native rows close the selected root's actual broker
transport while keeping its host/store alive. This establishes real disconnection
behavior, not automatic broker outage recovery.

Cross-author review found no required or optional source finding. The readiness
author independently reviewed quarantine, and the quarantine author independently
reviewed pressure and readiness, including resolution and the next legitimate
operation. Both explicitly found no material product-responsibility or verification
machinery expansion. Existing bounded helpers and provider fixtures remain the
simplest direct approach; no new framework, schema migration, dependency, reconnect
owner, process fixture or test harness was introduced.

Actual native outcomes, corrections, exact source/binary custody and cleanup are
recorded in [verification](verification.md). Source review alone is not passing
execution evidence. The prior unrelated Heartbeat intermittency and single-node,
Linux-SDK, deployment and release evidence limits remain unchanged.

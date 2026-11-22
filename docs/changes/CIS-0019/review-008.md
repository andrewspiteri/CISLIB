---
title: "RM-04 typed request/reply and durable result recovery review"
type: document
status: Draft
---

Part B now uses the existing original RM-02 transaction, outbox, dispatcher,
Security native authority and RM-03 journal. A waiter and its bounded retained-byte
lease are registered before staging. Application-owned tickets stay disposable;
the caller completes its transaction, and waiting requires an independent known
committed receipt. Submission, exchange, protocol and business decisions remain
separate. Deadline/cancellation ends local waiting without asserting remote rollback.

Independent source reviews corrected exact owned cancellation-token identity,
first identified cancellation, retained borrowed decode bytes, notification-only
reply completion, preparation before waiting and joining owned ticket cleanup.
The valid small-payload profile exposed a writer growth-hint bound; that failure,
the earlier hung driver and its exact owned termination remain recorded. The
corrected Core matches the immutable 502/502 Windows/Ubuntu unit candidate.

The actual PostgreSQL/TLS RabbitMQ B01–B06 cohort passes 12/12 on Windows and
12/12 on Ubuntu, with zero failures/skips. It covers original success/rejection,
timeout and cancellation after independently verified remote commit, two live
caller endpoints, wrong authenticated responder/attempt quarantine before releasing
legitimate responses, changed-digest rejection, canonical duplicate reply identity
and count, current-attempt conflict/denied/expired/incompatible recovery and an
active original transaction. The B05 case kills both actual still-live child
process trees after effect/result/reply commitment, joins exit/output, then
recovers the original result at a new endpoint with new message/attempt IDs and
one non-idempotent SQL effect plus one finalized journal entry.

The real B06 case also retains an Unknown completion after an actual commit-boundary
failure and an independently Found receipt. Four active transaction-owned tickets
saturate the request budget; the next request is Overloaded without a new commit
or row. Actual automatic coordinator/ticket observations reach the existing HTTP
Metrics exporter: one submission_unknown, one overloaded and four cancelled
observations, positive duration, zero active, central identity and finite alias
labels. This revised case passes separately on Windows and in the Ubuntu 12-case
cohort; manual instrument calls are not substituted for this proof.

Current-Core refresh passes 26/26 on each runtime: two console/web host cases,
twelve collection/exporter cases, three publication, four dispatch, four
disposition and one fixed sixteen-message footprint case. The unchanged SQL
schema-v2 component retains 41/41 per lane and protocol/disposition component
56/56 per lane. The independent old-schema rejection, installer/embedded identity,
reply-anchor/fence/redrive/retention coverage is preserved. Wire remains v1;
installation is explicit, with no automatic migration.

The current 9.5-hour WORK-008 allocation includes all authors, reviewers, measured
native phases and controller source estimates; earlier 7.5/8.5-hour prose is
historical. Attempts, costs and restoration reserve were not reset. Immutable
source/binary/TRX custody is recorded in the ignored
`.artifacts/messaging/CIS-0019/WORK-009/final-integrated-receipt.json` and its
`final-integrated-candidate/manifest.json`. Test-only package-hash validation was
the only Integration source change after the final native behavior cohorts;
their bodies and Core bytes remain identical.

These are single-node developer runtime facts. They do not establish deployment,
multi-node failover, Linux SDK package building or release readiness. The existing
unrelated Heartbeat 119/120 Ubuntu cohort and isolated 1/1 pass remain intermittent
evidence; no Heartbeat source was changed.

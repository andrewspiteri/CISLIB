---
title: "RM-04 transactional dispatch and Operations binding review"
type: document
status: Draft
---

The Part A candidate passes 425 affected Messaging/Operations unit cases on
Windows and Ubuntu, and four real PostgreSQL/RabbitMQ dispatch cases on each.
The latter use concurrent native deliveries and an unkeyed SQL counter: the
counter itself supplies no idempotency protection. Shared inbox arbitration
prevents a second committed effect; another logical subscription remains
independent. A reconstructed receiver reuses the retained duplicate state.

The real-provider cases also establish fresh arbitration after Unknown commit,
separate denied/conflicting rejection records, and actual RM-03 Prepared,
new-message Replayed and changed-business-input conflict behavior. The one-shot
original-context guard checks the actual trusted resolved resource before
authorization, journal access or effects. Its public Operations API is unchanged.

Independent source review covered that guard, dispatcher cleanup/acknowledgement,
bounded encoding and asynchronous capacity admission. The writer preserves the
wire format while bounding allocated segments and flattening; cancellation and
drain retain leases until actual ownership ends. The observed-memory test was
corrected to avoid counting its setup getter as a dispatcher read. No analyzer,
authorization, assertion or native settlement requirement was relaxed.

The preserved candidate and all four result files are bound by
`.artifacts/messaging/CIS-0019/WORK-005/part-a-component-receipt.json` and
`part-a-candidate-1/manifest.json`. Subsequent host-fixture compilation has a
separate candidate-2 receipt. These are Part A component facts; typed request
recovery, final package integration and Part B acceptance remain separate work.
WORK-010 and WORK-011 continue the original WORK-005 outcome and accounting owner.

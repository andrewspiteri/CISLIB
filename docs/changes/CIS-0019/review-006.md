---
title: "RM-04 failure disposition and administration review"
type: document
status: Draft
---

The current Part A candidate passes the disposition/administration cases within
the 425 affected unit cases on both Windows and Ubuntu, plus four real PostgreSQL
disposition cases on each. The original failures, corrections and measured costs
remain in the native journal. Exact inputs/results are retained in
`.artifacts/messaging/CIS-0019/WORK-006/part-a-component-receipt.json`.

Independent review checked fresh rollback-gap arbitration, immutable canonical
identity, preservation of successful/later winners and distinct rejection state.
Only a known NotCommitted canonical attempt can secure a scheduled retry or
terminal handoff. Unknown remains recoverable and cannot become a rollback claim
from an absent lookup. Invalid input cannot reserve or poison a legitimate inbox.

Administration authorizes before SQL, bounds inspection/maintenance, reserves
shared work and historical body bytes, and stops admitting new calls immediately
at runtime cutoff. Retention preserves unresolved handoffs. Audits describe the
actual settled transaction even when subsequent cleanup fails; the original
failure is preserved if audit also fails. Indefinite retention remains the default.

Part B must additionally attach canonical terminal replies and safe authorized
rejection-linked Denied replies atomically, protect those links during cleanup,
and clear old-generation reply references on explicit redrive. This review does
not claim those future additions already exist or reopen Operations maintenance.

---
title: "RM-04 PostgreSQL store review"
type: document
status: Draft
---

The independent reviewer examined the seven production PostgreSQL sources and
installer SQL, real fixture, tests and provider guide. A required classification
defect was corrected: future ordinals/generations are rejected before returning
an existing Handled/Terminal winner. Paired regressions preserve both retained
state and outbox counts. A separate valid `long.MaxValue` capacity edge now uses
branch-local CASE arithmetic, including negative cleanup accounting. Original
contexts, RC arbitration, fencing, atomic handoffs, immutable capacity mode and
retention responsibility have no remaining required source finding.

Native alignment review checked every entry of the ignored
`.artifacts/messaging/CIS-0019/WORK-002/store-receipt.json`: 13 source files,
four binaries/runtime metadata and two actual TRX files. All hashes match;
source and IntegrationTests Core copies match. Windows and Ubuntu each report
33 executed/passed, zero failed/skipped, with PostgreSQL 18.7. The affected
Release build has zero warnings/errors. Scoped whitespace formatting and
verification passed; the two subsequent explicit test-overload corrections
remain in the next provider formatting check.

This establishes the bounded storage implementation. Broker delivery,
transactional dispatcher, request/reply, public-package and release claims are
separate. Review costs remain in the same finite WORK-002 allocation: 45 minutes
of independent SQL/native review plus the author's separately recorded 240
minutes and earlier root preparation. External estimates are not CIS automatic
attempts or consumption.

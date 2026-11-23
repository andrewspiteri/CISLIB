---
title: RM-06 publication bridge and host checkpoint
type: document
status: Draft
change_id: CIS-0023
---

# Core publication, bridge and host checkpoint

WORK-002/003 establish the logical producer and actual owned host boundaries.
WORK-005 completes the two portable Security detail contracts with the existing
evaluator. SQL/broker/public-package/example and Ubuntu acceptance remain in
WORK-004; these results do not claim destination archive persistence.

| Selected native check | Actual result |
| --- | --- |
| Capture/publication/hosting units, including the pre-canceled startup guard | 102 passed, 0 failed/skipped |
| Explicit Security bridge mapping, result/deadline and suppression units | 15 passed, 0 failed/skipped |
| Common and Security schema/wire vectors | 63 passed, 0 failed/skipped |
| Actual optional collector/exporter and Console/Web failed-Build cleanup | 9 passed, 0 failed/skipped |
| Existing affected application/workflow/host lifecycle regressions | 62 passed, 0 failed/skipped |

The selections have overlapping regression coverage; they are not an invented
aggregate release count. Affected Core/CommonTesting/unit/Architecture/Integration
builds completed with zero warnings/errors. Scoped ordinary pinned SDK whitespace
format and verification passed for the implementation files at that checkpoint.

The original transaction and immutable captured record reach the existing outbox
without an extra pre-admission encoded copy. Refusal/fault poisoning covers actual
rollback-only cleanup. Standalone results retain exact available receipts,
completion and cleanup; an impossible provisional state faults. A late known
commit remains committed. Ordinary scoped capture and singleton Security mapping
keep separate lifetime/trust inputs.

Admitted jobs can issue their final Audit calls. Audit then closes and joins actual
capture/publication work before signaling dependency teardown. The facade retains
the exact owners even when Build fails before the runtime is constructed. Native
Console/Web tests held a mapper across the original Build fault and proved Stop/
Dispose wait, closed admission, retained dependency use and borrowed-store ownership.
The optional collector likewise waits for actual definition-only capture drain.

One new-unit assertion variable name triggered MSTest's expected/actual analyzer;
the corrected build and actual tests passed. Sandboxed integration build could
not connect its existing x64 task host; the same pinned build succeeded with the
permitted sandbox override. Four authored native-test signature errors were fixed
without changing a fixture or product contract. All earlier results/costs remain.

Receipts are in `.artifacts/audit/CIS-0023/WORK-002/` and `/WORK-003/`;
portable Security completion is in `/WORK-005/security-contract-architecture-tests/`.
Source requirements and the user-owned launch settings remain preserved.

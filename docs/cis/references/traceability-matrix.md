---
title: "Logging task context"
type: reference
status: Draft
owner: Repository maintainer
review_cadence: on roadmap task change
cis:
  stable_id: cislib:reference:traceability-matrix
---

# Logging task context

This is a selective routing index for the current exception story. It connects existing task IDs to source and retained reports so CIS graph queries can locate their context. The [implementation status](../../../planning/logging/implementation-status.md), [case inventory](../../../conformance/logging/case-inventory.json), [user decisions](../../../planning/decisions.md) and [plan amendments](../../../planning/logging/plan-amendments.md) remain authoritative. Other roadmap tasks have not been migrated into this index.

Every row remains Draft in CIS. Recorded roadmap status reports existing qualification; it does not create a CIS acceptance or an approved plan. Implementation evidence identifies the behavior under qualification, including code unchanged by a qualification task. Verification paths lead to actual reports; a graph relationship does not establish execution.

| Trace ID | Requirement / source | Specification | Component | Evidence | Implementation evidence | Verification evidence | Status | Recorded roadmap state | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| WP04-S02-T01 | Original logging backlog WP04-S02-T01 | docs/modules/logging/exception-projection.md | cis-core | implementations/csharp/src/CIS.Core/Logging/Exceptions/ExceptionProjection.cs | Internal owned exception capture | planning/logging/evidence/wp04-s02-t01-windows-linux.json | Draft | Done | Foundation; bounded cooperative accessors; later event integration remains pending. |
| WP04-S02-T02 | Original logging backlog WP04-S02-T02 | docs/modules/logging/exception-projection.md | cis-core | implementations/csharp/src/CIS.Core/Logging/Exceptions/ExceptionProjection.cs | Existing capture bounds, ordered children and owned shape | planning/logging/evidence/wp04-s02-t02-windows-linux.json | Draft | Done | Qualification changes tests; shipping capture remains unchanged. |
| WP04-S02-T03 | Original logging backlog WP04-S02-T03 | docs/modules/logging/exception-projection.md | cis-core | implementations/csharp/src/CIS.Core/Logging/Exceptions/ExceptionProjection.cs | Existing local truncation and metadata exclusion | planning/logging/evidence/wp04-s02-t03-windows-linux.json | Draft | Done | Commit a3d28c8; 60 new native cases, 2810 total; Windows/Ubuntu process checks and copied-source replay. |
| WP04-S02-T04 | Original logging backlog WP04-S02-T04 | planning/logging/sources/core-library-backlog-v1.json | cis-core | Pending | Complete shared deterministic exception fixtures and final story qualification | Pending | Draft | Next | Not started; no completed execution or CIS acceptance is claimed. |

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
| WP04-S02-T04 | Original logging backlog WP04-S02-T04 | conformance/logging/exception-qualification-map.json | cis-core | implementations/csharp/src/CIS.Core/Logging/Exceptions/ExceptionProjection.cs | Existing capture qualified by shared descriptors and native adapters | planning/logging/evidence/wp04-s02-t04-windows-linux.json | Draft | Done | Existing roadmap task qualified with reviewed native/process/fresh-copy evidence. CIS lifecycle remains Draft; no product acceptance is claimed. |
| WP04-S03-T01 | Original logging backlog WP04-S03-T01 | docs/modules/logging/operation-scopes.md | cis-core | implementations/csharp/src/CIS.Core/Logging/Scopes/OperationScopeStore.cs | Copied context and per-store asynchronous scope storage | planning/logging/evidence/wp04-s03-t01-windows-linux.json | Draft | Done | Reviewed native/process/fresh-copy evidence qualifies T01; full owning-flow disposal and later tasks remain pending. CIS lifecycle remains Draft; no product acceptance is claimed. |
| WP04-S03-T02 | Original logging backlog WP04-S03-T02 | docs/modules/logging/operation-scopes.md | cis-core | implementations/csharp/src/CIS.Core/Logging/Scopes/CapturedScopeContext.cs | Bounded merged scope view and depth qualification | planning/logging/evidence/wp04-s03-t02-windows-linux.json | Draft | Done | Reviewed native/process/fresh-copy evidence qualifies T02. Full ownership and shared story remain open; no CIS product acceptance is claimed. |
| WP04-S03-T03 | Original logging backlog WP04-S03-T03 and user-selected ownership amendment | docs/modules/logging/operation-scopes.md | cis-core | implementations/csharp/src/CIS.Core/Logging/Scopes/OperationScopeStore.cs | Creator-owned LIFO/repeated disposal and precise detectable-state guards | planning/logging/evidence/wp04-s03-t03-windows-linux.json | Draft | Done | Reviewed native/process/fresh-copy evidence qualifies T03 under the selected caller ownership rule. Cross-flow misuse is prohibited without universal detection; T04 and public runtime integration remain open. No CIS product acceptance is claimed. |

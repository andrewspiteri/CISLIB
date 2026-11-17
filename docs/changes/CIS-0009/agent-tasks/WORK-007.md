---
title: "WORK-007 Permit the approved DiagnosticSource transitive package mapping"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-007
parent_id: none
category: implementation
complexity: low
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:a3f73b3bd89d4a6e8fa8972814cf4de8"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0009:outcome:56b4c76df486408d8d80b6e0a124c37e"
authorization_scope_ids: ["metrics","metrics-package-sources"]
authorization_scope_basis: "sha256:df2436b1b6a3515c7824f96a418b32fb65830a9f4308d702e8d5b933a9487b06"
---

# WORK-007: Permit the approved DiagnosticSource transitive package mapping

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Permit the approved DiagnosticSource transitive package mapping

## Acceptance criteria

- [x] Add only the separately approved System.Diagnostics.DiagnosticSource exact source-mapping entry to existing nuget.org; preserve all prior sources, all five approved OpenTelemetry mappings, Core closure, SDK/runtime pins and shared limits. This supports the same optional Metrics package and creates no new delivery outcome or resource allowance.

## Targeted validation

- [x] Parse actual XML and compare against the completed WORK-006 five-ID mapping: exactly one additional System.Diagnostics.DiagnosticSource ID, no source or earlier mapping changes. Reuse actual WORK-002 locked restore/build/native results as integration evidence without duplicate execution; confirm the optional consumer deploys DiagnosticSource11 while Core-only closure remains unchanged.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Exact mapping | Only DiagnosticSource added after the approved five mappings; all original sources and earlier mappings retained | nuget-mapping-comparison.json; verification.md |
| Actual dependency deployment | Locked collection31 and final isolated package build/publish passed on.NET10.0.12 with privateDS11; Core closure unchanged | metrics-collection-integration and metrics-package-windows native results |
| Shared limits | No new allocation or native rerun charged to support owner; all integration costs retained by existing implementation owners | CIS work record; verification.md |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1142; failed=167; possibleTokenSavings=310; ledgerDigest=sha256:eca6f14cca9bb00c5b7c8a13bd9cee352b7b08afeb583b992cdfa04a0f6fd499 |

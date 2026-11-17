---
title: "WORK-006 Enable the approved optional Metrics NuGet source mappings"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-006
parent_id: none
category: implementation
complexity: low
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:b63cb17e15ea4b51b4b19a4758e1acce"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0009:outcome:56b4c76df486408d8d80b6e0a124c37e"
authorization_scope_ids: ["metrics","metrics-package-sources"]
authorization_scope_basis: "sha256:df2436b1b6a3515c7824f96a418b32fb65830a9f4308d702e8d5b933a9487b06"
---

# WORK-006: Enable the approved optional Metrics NuGet source mappings

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Enable the approved optional Metrics NuGet source mappings

## Acceptance criteria

- [x] Apply only the explicitly approved five OpenTelemetry package source-mapping entries in the existing NuGet.config; preserve all previous sources/mappings, Core dependency closure and finite limits. This is supporting package-configuration work under the existing Metrics outcome, not a new delivery outcome or budget.

## Targeted validation

- [x] Parse the actual XML; independently compare the five added IDs and all pre-existing source/mapping entries against main. Retain the actual optional-package restore/build result from WORK-002 as integration evidence without duplicate execution. No new source, wildcard or disabled package mapping.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Exact mapping delta | Passed | `.artifacts/metrics/CIS-0009/package-mapping-verification.json`: exactly five added IDs, no removed mapping/source changes. |
| Actual optional restore | Five selected OTel packages resolved; overall restore failed on additional DiagnosticSource11 mapping | WORK-002 `metrics-collection-integration/latest-failure-stdout.log`; no native collection test or compatible-build pass claimed. |

The approved five-ID support change is complete. The separately prepared sixth
transitive-package mapping remains pending a targeted human decision; no change
to the original five-ID executable contract, existing sources or Core closure.
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1057; failed=157; possibleTokenSavings=123; ledgerDigest=sha256:ddb267e07e608c223e3b2554f99d231da59a47e8a62ba5f47dbc8e71f9639d29 |

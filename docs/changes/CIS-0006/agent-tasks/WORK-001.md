---
title: "WORK-001 Define Heartbeat consumer contracts, copied configuration and weak registration ownership"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-001
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:seed:becde80cd5184b4e86cfeba58824533a"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0006:outcome:1b263f451fd24affb01271c9f2ed4f66"
authorization_scope_ids: ["heartbeat"]
authorization_scope_basis: "sha256:7be3625710d72f724d009b5375a3779a25ee5f2d9cab24c29dd6bb63fe8284e8"
---

# WORK-001: Define Heartbeat consumer contracts, copied configuration and weak registration ownership

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Define Heartbeat consumer contracts, copied configuration and weak registration ownership

## Acceptance criteria

- [x] AC-1/AC-4/AC-7: provide the two-method consumer registration API and awaited subscriber contract; snapshot10ms/default-zero schedule and validate documented duration/name limits; resolve MaxConcurrent once with invariant environment/setup/default precedence and1..16 range. Preserve actual EnvironmentOptions architecture. Define one actual owner with a weak idle association and atomic lifetime-hold contract used by the engine; no alternate scheduler, subscriber disposal transfer or generic startup framework. Include meaningful configuration/identity/reference-graph tests and independent review.

## Targeted validation

- [x] Build affected Core/test projects, run the focused new Heartbeat configuration/contract tests plus affected EnvironmentOptions architecture/constructor assertions. Inspect public XML contract and bound-condition/owner graphs. Record actual native results and applicable source AT coverage; preserve old module defaults and dependency closure. No release qualification claim.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Contract/configuration/ownership and independent review | Implemented; two-method consumer API, copied validated settings, weak conditional binding and counted real owner holds reviewed. No unresolved required product defect. | Heartbeat source, module guide and final review in [verification](../verification.md). |
| Actual native verification | 171 module/configuration/receiving cases and 305 architecture cases passed; zero failed/skipped. | `.cis/local/testing/incremental/heartbeat-module-unit/reports/file-e7e4df149e0949e4a8a2-unit.xml`; `heartbeat-architecture` corresponding report. |
| Owned settlement | WORK-001:1 closed after all actual invocations settled; failures/costs preserved. | Canonical CIS work ledger; final checkpoint and [verification](../verification.md). |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=686; failed=108; possibleTokenSavings=123; ledgerDigest=sha256:a9472fd9171ccee25afeb61751ff4e2a82e8dd8d6ac9d00f2f000c6e477c48f5 |

---
title: "WORK-001 Define Configuration contracts and retained-root read/event ownership"
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
instance_key: "outcome:seed:4d0acc22f5cd472cbadc06f4e20a19fa"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0008:outcome:cb8dfe5d5f71451f94bc5d7743cfcb5e"
authorization_scope_ids: ["configuration"]
authorization_scope_basis: "sha256:0321dc009b4b64ac159f1b1d5dcd1f25709f61e15500133956d713330e600af1"
---

# WORK-001: Define Configuration contracts and retained-root read/event ownership

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Define Configuration contracts and retained-root read/event ownership

## Acceptance criteria

- [x] Under the selected P01–P08 profile, preserve existing diagnostic names; establish explicit marker/event, provider/result, retained-entry/default/prepared-value and reader contracts without a framework. Reuse actual Type Manager query for concrete closed candidates, report incompatible types, construct exactly once via eligible constructor or explicit exact-type factory and capture defaults once. Own each root/gate per runtime, stable type/assembly storage identity with fixed host scope/collision checks, synchronized short reads and helper-backed direct-interface/base event subscriptions with useful incompatible-wiring detection. Include tests/docs/review in this owner; no mapper, backend or scheduler hidden in contracts.

## Targeted validation

- [x] Focused native identity/host isolation/retention, key and host collision/version policy, constructor/eligibility/default capture, exact-instance reader rejection, direct-interface/optional-base event wiring and negative architecture guards. Establish meaningful Core+Unit builds after actual allocation; inspect names against existing CIS.Configuration and approved dependency closure. Freeze shared seams before W2/W3 and record exact symbols/results, scope/review and real cost.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Core and Unit compilation / focused Unit scope | 619 passed, 0 failed, 0 skipped, including 42 new foundation cases | configuration-objects-unit retained receipt and TRX; first compiler failure/correction preserved in verification |
| Discovery boundary architecture | 5 passed, 0 failed, 0 skipped | configuration-discovery-architecture retained receipt and TRX; production guard plus four negative controls |
| Source, documentation and independent review | Shared contracts frozen; no required defects after the internal-name repair | Configuration/Contracts and Objects; docs/modules/configuration/core.md; verification.md |
| Real Type Manager boundary | Actual completed query used; valid selected candidate exercised by primitive catalog test | Full unfiltered module bootstrap remains WORK-004 and is not claimed here |
| Accounting and physical storage | 795421 inclusive native milliseconds, 1 logical attempt; zero reservations | Shared CIS-0008 grant, counted sample 5417864203 B with 67108864 B reserve preserved |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=902; failed=131; possibleTokenSavings=123; ledgerDigest=sha256:048cb6234fb06b652746ba2c63346201ce4bc2ca450cc14d0a13c266da39c8a1 |
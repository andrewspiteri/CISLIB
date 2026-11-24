---
title: "WORK-006 Compose same-session pre-Build bootstrap and sequential host activation"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-006
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:3a56e0f694854567b48b018699e1078d"
depends_on: ["WORK-004","WORK-005","WORK-007"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0027:outcome:0b715aa498aa4ef7a0ab5458f32b51a3"
authorization_scope_ids: ["bootstrap","records"]
authorization_scope_basis: "sha256:f6fd40a760c59971a2ac856c6adf642744b21892f26f9835f216b55b80ce1e01"
---

# WORK-006: Compose same-session pre-Build bootstrap and sequential host activation

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Compose same-session pre-Build bootstrap and sequential host activation

## Acceptance criteria

- [x] Live authenticated session and paired central values before consumers, same registration/cohort/authority handed into single provider, Config activation epoch consumed before initial Management control hook, bounded reconnect epochs without starvation, fresh control after slow Build and correct actual failed-Build/stop ownership.

## Targeted validation

- [x] Real constructor-visible values, live-empty defaults, absent/unavailable bootstrap/cache refusal, credential handoff, cancellation/failed Build, activation gap, stale authority, outage/reconnect and intact rejection/pending restart cases/docs.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Current Core units | 1,200 passed; zero failures/skips | WORK-006/current-host-order-affected-unit-tests/results/current-host-units.trx |
| Explicit native credential activation | 15 PEM/SDK preparation cases passed, including actual drain/cancellation and explicit final activation before ordinary Startup | WORK-006/explicit-native-preparation-activation-tests/results/prepared-activation.trx |
| Actual composed host | Five SDK/TLS/PostgreSQL cases passed: constructor-visible roots, same registration, final meter identity, outage/restart convergence before hourly poll, live empty release, required missing/unassigned refusal and held failed-Build cleanup | WORK-006/native-bootstrap-live-authority-final-tests/results/native-bootstrap.trx |
| Existing facade behavior | All 64 existing Application cases passed; only two new identity-fixture assertions failed in that run and were corrected/rerun | WORK-006/native-bootstrap-reconnect-tests-corrected/results/native-bootstrap.trx |
| Review and formatting | Native handoff independently reviewed; narrow gap and cached-failure findings corrected, ordinary order validation preserved; scoped verification passed | ../review.md; native-costs.jsonl phase core-host-provider-closing-format-verify |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2440; failed=332; possibleTokenSavings=591; ledgerDigest=sha256:98ab81d26b18fcb17d5aed3428552101cfab3dc6f4383b62a3544e67e529f43a |

---
title: "WORK-004 Instrument bootstrap Startup Configuration and Type Manager facts"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-004
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:a8f091f25d964873b0c0347355f1b50b"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0009:outcome:56b4c76df486408d8d80b6e0a124c37e"
authorization_scope_ids: ["metrics"]
authorization_scope_basis: "sha256:ce451dd7eb927ccdd3a6aa8473f946cddc546f9d4161af350f43a8a47ccd8c7d"
---

# WORK-004: Instrument bootstrap Startup Configuration and Type Manager facts

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Instrument bootstrap Startup Configuration and Type Manager facts

## Acceptance criteria

- [x] Observe actual sequential Startup stage/participant outcomes with correct duration origins; Configuration load/refresh/remap and current/prepared facts; Type Manager final retained report counts/duration. Preserve existing constructors and startup/lifetime/readiness/refresh behavior, and keep coordinator independent of SDK/provider runtimes. Early pre-DI facts use bounded retained terminal snapshots without event replay, extra discovery or provider. No sensitive values or arbitrary CLR-type labels. Do not edit shared hosting/project/provider files; own focused catalog/tests/docs/review.

## Targeted validation

- [x] One actual normal/failure path per changed module, including pre-DI preparation before provider and live facts after activation; no event replay or rerun of discovery/load. Confirm existing sequential construction/initialization, held cancellation/stop, Configuration same-root/read/events and Type Manager full metadata/identity remain intact. Use native tests and update module guide with exact timing/availability boundaries.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Native focused Unit | 13 passed,0 failed/skipped | metrics-bootstrap-unit TRX and state; real Core compilation. |
| Independent review | No remaining blocking finding | C/D paired source review, explicit ITimer aliases and existing TemporaryDirectory helper. |
| Accounting | One logical attempt,461710 inclusive measured native ms; no reservation/hold | Initial shared test compilation failure225274ms plus successful236436ms; costs retained. |

Actual optional provider attachment and host integration remain WORK-005 scope.
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1074; failed=158; possibleTokenSavings=123; ledgerDigest=sha256:8ddcc90b9742308d859672979a9de9c4849ab51e62564648e9e43cbdc059d376 |

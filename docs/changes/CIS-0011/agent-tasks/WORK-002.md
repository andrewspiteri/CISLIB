---
title: "WORK-002 Implement retained application lifecycle and real web readiness integration"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-002
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:dad7ac7c64d64aceadf21127ddb5e0c1"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0011:outcome:ad93dd647e1f41ad9dac004ce6061dda"
authorization_scope_ids: ["application-facade"]
authorization_scope_basis: "sha256:2ba6ff96550e6f8403209e849521c15a7c0224d9e53d4a659df08abb25579550"
---

# WORK-002: Implement retained application lifecycle and real web readiness integration

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Implement retained application lifecycle and real web readiness integration

## Acceptance criteria

- [x] AP2 / APP-02,APP-03,APP-06,APP-07: retain one start/stop/dispose operation and one host/provider. Start joins preparation and actual host/CIS startup; Run starts, waits for ordinary shutdown and stops, returning an exit result only for observed outcomes with retained cause. Stop signals owned work before actual startup/configuration/heartbeat/REST/metrics drains; disposal handles partial Build/start acquisition and propagates cleanup faults safely. Use native routing/operational branches and existing health/business readiness statuses, real early listener access, required sequential tail bridge order and one host disposal path. Include real-host/ownership tests, docs and review.

## Targeted validation

- [x] A02–A05/A10: all startup roots constructed before sequential identity-deduplicated starts; repeated/concurrent calls share retained outcomes and terminal stop cannot restart/readiness-reopen. Held real initializer proves health500/business503 then ready200 without side effects. Held work/canceled waits/partial build prove no dependency disposal before actual settlement; original start/cancel/fault and later disposal errors surface correctly.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Windows native host | 17/17 passed; retained lifecycle, startup ordering, readiness, actual drains, logging ownership and failures | application-host / native/incremental/d5ca5f01cd724a44a839d6a8b72461ca; host-observed.trx |
| Review and guide | Independent lifecycle/Metrics review resolved; native provider and coordinator contracts retained | docs/modules/application/core.md; docs/modules/metrics/hosting.md; CIS-0011 verification.md |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1451; failed=215; possibleTokenSavings=591; ledgerDigest=sha256:e4959cac39606a98a076e36b6ae1802cc7b1c1723ddbbb2311448020ae5262ec |

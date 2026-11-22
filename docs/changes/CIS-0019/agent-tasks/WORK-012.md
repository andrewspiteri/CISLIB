---
title: "WORK-012 Qualify sequential Messaging startup and existing host drain"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-012
parent_id: WORK-007
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: "WORK-007"
instance_key: "outcome:ce8caea6e843486f91c059c214ddd4f3"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0019:outcome:7f1507a223264cf1986c6524aedc5a6c"
authorization_scope_ids: ["composition"]
authorization_scope_basis: "sha256:ca5b0f1b7ab2b6b8870d5c715e51d913385df2f872c4ce516f39c93283f14611"
---

# WORK-012: Qualify sequential Messaging startup and existing host drain

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Qualify sequential Messaging startup and existing host drain

## Acceptance criteria

- [x] Finish existing WORK007 P16 hosting/facade integration under current corrected path scope: passive registration and exact immutable composition; sequential schema/store check then native transport then publisher start; readiness and actual caller/dispatcher/consumer/publisher/native/shared-budget/monitor drain before existing Metrics/Heartbeat/data-source teardown. Preserve public constructors and independent module runtime, no new lifetime/DI framework. Final acceptance requires completed WORK004, WORK006 and dispatch continuation WORK010/WORK011.

## Targeted validation

- [x] Console/web actual PG+Rabbit facade tests, held original transaction and scoped cleanup, cancellation of outer wait without early dependency release, original cleanup faults and concurrent disposal joining. Focused bounded lifetime/admission tests, existing affected host regressions, scoped style/build, host guide and independent lifetime review. Reuse provider proof only at matching material inputs.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| PartA integrated acceptance | Actual host2/exporter12 each; package2appsWindows, footprint1each, 425Messaging/Operationsunits each; prior119/120Heartbeat cohort and isolated1pass retained | [Review](../review-007.md), [verification](../verification.md), `.artifacts/messaging/CIS-0019/WORK-007/part-a-host-observations-receipt.json` |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2029; failed=281; possibleTokenSavings=591; ledgerDigest=sha256:d2b6befdf3254a6c50e71579103627c3fdde3f1ccf3ca48a58bf27e5f988a07c |

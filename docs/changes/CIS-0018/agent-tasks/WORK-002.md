---
title: "WORK-002 Implement authorized durable execution and independent committed-result lookup"
type: agent-task
status: Draft
task_status: Draft
task_id: WORK-002
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:c25f3b39d30340c79dd38899eaeb39c8"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0018:outcome:a5124e88d31b486fafd34f4241c8fa4c"
authorization_scope_ids: ["execution-recovery"]
authorization_scope_basis: "sha256:71db59ea67c7b9aa46a4d4c10e600b01d9ccff7ee4566c9b680f5cb1892cbf01"
---

# WORK-002: Implement authorized durable execution and independent committed-result lookup

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Implement authorized durable execution and independent committed-result lookup

## Acceptance criteria

- [ ] P04-P08/P11/P13 and AC02/03/06/07/08/10/12: one nongeneric module-wide weak context admission across instances/types, native claim/read then outside-callback handler then native terminal write; exact original context, separate Prepared/Replayed/business/persistence facts. Existing capability authorization before first/replay/lookup; after-claim between-callback failures poison and preserve originals. Fresh authoritativeRC lookup owns only its read resources and preserves known committed facts plus read cleanup diagnostics.

## Targeted validation

- [ ] Controlled store/ADO tests for cross-instance/generic nested/repeated/concurrent admission, exactcontext/DB/isolation preflight, input snapshot, authorization/denial/audit fault/cancellation beforestore orhandler, nativecallbacknonreentry, handler/encode/size/write/CT faults, rollbackonly refusal, exact resultfidelity/version/conflict/expiry precedence and freshlookup while claim uncommitted. No automatic ownercompletion/retry; docs/style/build and independentreview.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

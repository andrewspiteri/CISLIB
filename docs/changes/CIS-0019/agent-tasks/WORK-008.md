---
title: "WORK-008 Deliver typed request/reply and current-attempt durable recovery"
type: agent-task
status: Draft
task_status: Draft
task_id: WORK-008
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:f03671ca53f34b78bb2363bde61de56a"
depends_on: ["WORK-007"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0019:outcome:7f1507a223264cf1986c6524aedc5a6c"
authorization_scope_ids: ["rpc"]
authorization_scope_basis: "sha256:640f9401a280b4f85f1f995c657afa48ed9369cb8fde63c83ab44b1f11b0ed26"
---

# WORK-008: Deliver typed request/reply and current-attempt durable recovery

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver typed request/reply and current-attempt durable recovery

## Acceptance criteria

- [ ] P17-P19 and B01-B06: Query/DurableCommand bindings on same store/dispatcher. Reservewaiter/registerbeforestage; originaltransaction disposableticket retains earlyresponse andmonotonicdeadline; knowncommittedreceiptbeforewait. Exacttrustedreplymatch/oncecompletion/currentattempt resultrecovery, endpointretirement and no remoterollbackorTasks claims.

## Targeted validation

- [ ] Realfastquery/durabledecision/rejection, timeout/cancelaftercommit/late/duplicate/maliciousreply, concurrentcallers/restart/response loss thennewattempt recoverywithoutsecondcommittedeffect; conflict/denied/expired/incompatible result; active/unknownsubmission/overload/capacitycleanup. Guide/style/build and independentrecoveryreview; metrics/package facts refreshedatPartB.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

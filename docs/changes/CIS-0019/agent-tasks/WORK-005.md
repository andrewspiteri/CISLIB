---
title: "WORK-005 Implement authorized transactional dispatch and durable operation binding"
type: agent-task
status: Draft
task_status: Draft
task_id: WORK-005
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:2971f23a8b8144ac82e79bd0e24f820c"
depends_on: ["WORK-002","WORK-003"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0019:outcome:7f1507a223264cf1986c6524aedc5a6c"
authorization_scope_ids: ["dispatch"]
authorization_scope_basis: "sha256:bb540bd23186b3cf932767351bb75e19f05b50f3eebcef20aa9ac7d722c372ae"
---

# WORK-005: Implement authorized transactional dispatch and durable operation binding

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Implement authorized transactional dispatch and durable operation binding

## Acceptance criteria

- [ ] P10/P11/P19 and A04-A06/A12: independent established native SecurityHostContextScope/DI scope, capability checks and originalcontext inboxclaim beforeeffects. Handleroutsidecallback, exactlyoneRM03attempt, atomicbusiness/operation/inbox/outgoing/reply and knowncommitbeforeack. Duplicate/replay/newattempt distinctions and Deniedrollback preserve legitimate canonicalidentity.

## Targeted validation

- [ ] Real independentinstances/subscribers/restart/concurrentduplicateeffect proof; inputdigestvsoperationconflict, denial/audithook/cancellation, prepared/replayed/completion/Unknown, exactsecuredreplyduplicate and actualscope/nativecleanup. Current source/native review, docs/style/build.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

---
title: "WORK-005 Implement authorized transactional dispatch and durable operation binding"
type: agent-task
status: Draft
task_status: InProgress
task_id: WORK-005
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:2971f23a8b8144ac82e79bd0e24f820c"
depends_on: ["WORK-002"]
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

- [ ] P10/P11/P19 and A04-A06/A12: independent established native SecurityHostContextScope/DI scope, capability checks and originalcontext inboxclaim beforeeffects. Handleroutsidecallback, exactlyoneRM03attempt, atomicbusiness/operation/inbox/outgoing/reply and knowncommitbeforeack. Duplicate/replay/newattempt distinctions and Deniedrollback preserve legitimate canonicalidentity. Source may proceed against the frozen provider ports after completed WORK002. Final native acceptance still requires completed WORK003. Add only a narrow exact-original-context expected-binding guard in existing Operations admission and coordinator before effects; public Operations contracts remain unchanged. Capture optional trusted-provenance retry validator and reserve one Core reply subscription alias, with passive collision validation.

## Targeted validation

- [ ] Real independentinstances/subscribers/restart/concurrentduplicateeffect proof; inputdigestvsoperationconflict, denial/audithook/cancellation, prepared/replayed/completion/Unknown, exactsecuredreplyduplicate and actualscope/nativecleanup. Current source/native review, docs/style/build. Include actual resolver-drift-before-effects regression; existing affected Operations tests. Complete only after WORK003 and integrated provider proof. No second security/DI/operation framework.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

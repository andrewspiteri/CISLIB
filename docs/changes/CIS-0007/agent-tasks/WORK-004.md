---
title: "WORK-004 Preserve immediate Heartbeat admission cutoff during held startup cancellation"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-004
parent_id: none
category: implementation
complexity: low
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:051f3eb2b711437cab8e643f988c1f41"
depends_on: ["WORK-003"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0007:outcome:c997f879fbcb418ca460744ba3ea25f0"
authorization_scope_ids: ["startup"]
authorization_scope_basis: "sha256:104bba5c3a57048c726589173933e59ceb6c67e313fdad8915953a164cf3d4f6"
---

# WORK-004: Preserve immediate Heartbeat admission cutoff during held startup cancellation

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Preserve immediate Heartbeat admission cutoff during held startup cancellation

## Acceptance criteria

- [x] Complete the same user-requested boundary refactor by preserving the original early Heartbeat admission cutoff: cancellation of the first real host startup token closes CreateOwner and Register immediately while an initializer remains held, before ApplicationStopping or explicit StopAsync. Keep all concrete wiring hosted, the coordinator independent and the one retained completion callback. Retain first-token/once-only subscriptions and actual drain before registration disposal. Include a deterministic actual-host regression, appropriate existing native tests, documentation and independent review. Preserve completed owners/evidence, user launch settings, APIs/dependencies and original shared limits.

## Targeted validation

- [x] Use the existing StartupHeartbeatCompletionTests host composition to hold an ignoring initializer, cancel only the first host-start token, prove ApplicationStopping remains unsignaled and startup/stop are not yet settled, assert new owner and new/duplicate registration reject immediately, then release and prove neutral cancellation, no dispatch/readiness and real cleanup. Run affected existing Unit/host Integration, dependency architecture and runnable source tests serially under the remaining original allocation. Preserve ordinary exclusions, do not create a framework, harness or tooling campaign.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Early cutoff and real settlement | Restored in hosting before construction; coordinator remains independent | StartupHostingExtensions.cs initial token subscription; FirstHostStartupCancellationClosesHeartbeatAdmissionWhileAnIgnoringInitializerRemainsActive |
| Final candidate native behavior | 181 Unit, 25 Integration, 341 Architecture, 11 BusinessCases passed; zero failures/skips | startup-host-boundary-final-* settled receipts and TRX; verification.md corrected-candidate section |
| Documentation and independent review | Guides and acceptance updated; no remaining material defect found | docs/modules/startup; planning/startup/acceptance.md; independent source/evidence/authority review |
| Authority, identity and storage | Same outcome/totals/reserves; 1982090 ms charge and one attempt; no reservation/recovery; sampled 5410530081 B | work-allocation-early-cancellation.json; canonical ledger; verification.md final hashes and observations |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=845; failed=125; possibleTokenSavings=123; ledgerDigest=sha256:b74a61b3ab549a34df44fae9a316c8c2235b9510e92eae4a8721f096864bbaeb |

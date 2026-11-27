---
title: "WORK-171 Qualify existing Metadata and Configuration operator action grants"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-171
parent_id: WORK-153
category: security
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: "WORK-040"
instance_key: "outcome:b21470f57d9e4828993da509555e78fe"
depends_on: ["WORK-151","WORK-152"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["capability-contracts","configuration-portal","intake-design","integration","portal","scheduler"]
authorization_scope_basis: "sha256:99bbd8947743415b377d7f2f5904b68fa36dc4a2af26666042adb57fdff5d0c6"
---

# WORK-171: Qualify existing Metadata and Configuration operator action grants

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Qualify existing Metadata and Configuration operator action grants

## Acceptance criteria

- [x] Deliver finite existing Configuration source/binding administrative action eligibility using actual stored targets and current native ICapabilityAuthorizer. Metadata replacement and Configuration publication/release/assignment submission and replay require same-resource original-result recovery grant bundles; mutations and recovery independently reauthorize after preview or grant changes. Preserve independently validated human versus workload identity, existing strict Entra semantics and hidden-resource behavior, with no portal fallback/copied-claim/inventory authority. Only installed native actions are advertised. This is the existing RM09/RM08 action and grant slice; Scheduler Task/manual/cancel runtime remains unstarted under the explicit BRD contract-only steering, with its original obligations preserved in future leaves and scheduler-contract-followups.md. No Scheduler or complete RM09 acceptance is claimed.

## Targeted validation

- [x] Use the existing native Management auth/HTTPS fixture for source/binding and metadata grant bundles, write without lookup, wrong-resource lookup, direct new/replayed writes, independent recovery after write revocation, hidden targets, unsupported backends and actual HTTP2 body framing. Retain native41 passed0 failed0 skipped, zero-warning build, scoped format and independent review evidence. Document exact native resources, finite action IDs and honest Scheduler unavailability. Consume only WORK-151/WORK-152 prerequisites and the existing WORK-040 parent pool; no new capacity or Scheduler runtime authority.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Existing native behavior | Passed: same-resource execute/recovery bundles, independently reauthorized lookup, hidden targets and finite installed source/binding actions | Original WORK-153 source and `.cis/local/testing/rm09-completion/work171-evidence.json`; all12 source SHA-256 values rechecked unchanged |
| Focused native verification | 41 passed,0 failed,0 skipped; real isolated PostgreSQL/RabbitMQ/JWT/HTTPS, including negotiated HTTP2 body rejection | `rm09c-153-provider-tests-corrected`; retained TRX and independent guard |
| Build and readability | Zero warnings/errors; scoped CSharp whitespace format plus verify | Original `rm09c-153-integration-build-corrected` and format phases |
| Independent review | Findings resolved; original39pass/1fail retained | `work153-grant-review.md`, `work153-actions-review.md` and corrected source/native hashes |
| CIS/source preservation | Native graph59131 nodes/344510 edges;0 errors/90 documentation warnings; all5 approved canonical documents unchanged | `rm09c-153-native-fresh-graph`, exact verified original graph checkpoint; physical peak remains sampled, not continuously established |
| Scope and accounting | Original153 retained Decomposed; futureScheduler172 remains Draft; same040 parent pool and historical costs | `rm09c-153-split-existing-future`; no Scheduler runtime/full portal/push/merge/release claim |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2936; failed=368; possibleTokenSavings=591; ledgerDigest=sha256:e9ab3191e6b3c6bb02580c3c02082119a22221380a904e098ecff466b3e42c2c |

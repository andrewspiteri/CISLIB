---
title: "WORK-004 Prove integrated Audit delivery and publish consumer guidance"
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
instance_key: "outcome:44962f7671644b868c1a488c72732a5f"
depends_on: ["WORK-002","WORK-003"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0023:outcome:b5a77a9ea98644b98e9eb55f5a83d096"
authorization_scope_ids: ["native-consumers","capture-contracts","publication-hosting","security-bridge"]
authorization_scope_basis: "sha256:563fc606a9e423513eae59ee13a2477bdbafc2e996a7c9f12bc42bf1f8cfca8d"
---

# WORK-004: Prove integrated Audit delivery and publish consumer guidance

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Prove integrated Audit delivery and publish consumer guidance

## Acceptance criteria

- [x] AC04-08/11-12 integrated: actual PostgreSQL business+Audit outbox commit/rollback/unfinished owner, ignored Audit pre-Messaging refusal poisoning, actual RabbitMQ delivery after source reconstruction with retained IDs/bytes, distinct MessageIds per deliberate same-AuditId submission, finite outer Operations/Message pressure settlement and no cross-delivery recursion. Outside-friend public Core/CommonTesting consumer, opt-in CountryData producer and focused receiving probe; one guide, portable receiver obligations and concise Audit skill/routing.

## Targeted validation

- [x] Strict selected existing Testcontainers fixtures with real durability/TLS/owned cleanup and zero skips; no new project/process mode/provider/store. Actual Windows focused source/package and Ubuntu managed units; truthful unavailable Linux-service limits. Affected compatibility/example/default/dependency checks, installed skill validation and realistic independent review. Preserve source/user hashes, storage/time/attempt journals and all earlier failures; no remote publication/release or broad campaign. Correct only defects demonstrated by the required integrated acceptance within these already-approved Audit/cooperating-host scopes; preserve completed task histories, no exploratory redesign or tooling campaign.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Native transaction/delivery/pressure/ingress behavior | Passed | 14 distinct actual PostgreSQL/RabbitMQ cases, zero failed/skipped; original 13-case selection plus corrected denied-grant case, both strict report guards passed. [Verification](../verification.md). |
| Current portable/runtime/public consumers | Passed | 117 Windows and 117 actual Ubuntu managed-unit cases; 63 portable schema/wire cases, 1 outside-friend package consumer and 7 CountryData examples, zero failed/skipped. Native Linux-client service execution is not claimed. |
| Affected contracts and hosting | Passed | 1 public API and 1 shipping dependency check; 9 actual collector/facade and 62 existing host regression cases, zero failed/skipped. Final affected builds have zero warnings/errors. |
| Documentation, formatting and independent review | Passed | One consumer guide and portable receiver obligations, validated Audit skill, scoped ordinary SDK format plus verification, local links/whitespace and independent capture/host/Security/native/package reviews. |
| Resource and user-input preservation | Passed | All source/failed command/attempt costs retained; shared allocation and reserves intact, counted storage below 6 GiB, final read-only inventory empty. Exact source and user launch-settings hashes preserved; no dependency/pin/project changes. |
| Accepted boundary | Preserved | No destination store or durable AuditId conflict enforcement, new packages, existing database access, push, merge, release or broad qualification campaign. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2271; failed=312; possibleTokenSavings=591; ledgerDigest=sha256:636183fbad8552c57b6566fc63be790ff592e2a747f0472e971e80561b45eeea |

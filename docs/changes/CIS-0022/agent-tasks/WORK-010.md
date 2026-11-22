---
title: "WORK-010 Complete dependency-preserving coordination hosting and native security drain"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-010
parent_id: WORK-006
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: "WORK-006"
instance_key: "outcome:fa7ed38d7f3c4442a59ae131f249c15f"
depends_on: ["WORK-001","WORK-003","WORK-004","WORK-005"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0022:outcome:31de62abefb947ed8b11c51cc37ba560"
authorization_scope_ids: ["hosting-observations"]
authorization_scope_basis: "sha256:a8c80fbb6b6c75e20535a07dc7d13a52fc2e72788769ac0ae9a134f52faf646e"
---

# WORK-010: Complete dependency-preserving coordination hosting and native security drain

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Complete dependency-preserving coordination hosting and native security drain

## Acceptance criteria

- [x] Complete existing sequential host/facade activation, trusted scoped authorization, bounded continuous supervisor, critical stopping readiness and coordination-first actual settlement before downstream modules/credentials/provider teardown, including failed startup/Build. Include original Entra native factory parameter for same internal lifetime. Preserve absence behavior, native ownership and original failures; include focused tests and documentation.

## Targeted validation

- [x] Focused known-module hosting, facade failed Build/start and callback dependency retention, startup order, uncooperative scope barrier, secure credential lifecycle and existing affected host tests; scoped format/build and review. No new host or DI framework.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Current pinned builds and scoped formatting | Passed | SDK10.0.401/runtime10.0.12, zero warnings/errors; see verification.md and retained hosting-native-proof.json. |
| Windows affected unit + actual native integration | Passed | 533/533 units and215/215 integration, zero failed/skipped; includes real HTTP scrape and actual failed Build. |
| Actual Linux and native credential ownership | Passed | 172/172 Linux units and7/7 existing Windows credential-owner regressions, zero failed/skipped. |
| Source, ownership and consumer documentation review | Complete | Known-module sequential host/barrier, original StartupCoordinator independent, bounded cached observations, entry captures; docs/modules/coordination and application/core.md reviewed. No cloud/failover/Linux SQL qualification claim. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2211; failed=301; possibleTokenSavings=591; ledgerDigest=sha256:ed8a509d792667714b65e54a46aedb1fe75a2017650bd794670027820e5e57d2 |

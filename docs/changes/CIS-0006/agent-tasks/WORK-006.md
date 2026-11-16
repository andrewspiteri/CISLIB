---
title: "WORK-006 Repair Type Manager compatibility consumer deployment and pinned API host discovery after Heartbeat integration"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-006
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:47811ae4b00c4b389171ad261f21a97f"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0006:outcome:1b263f451fd24affb01271c9f2ed4f66"
authorization_scope_ids: ["heartbeat"]
authorization_scope_basis: "sha256:7be3625710d72f724d009b5375a3779a25ee5f2d9cab24c29dd6bb63fe8284e8"
---

# WORK-006: Repair Type Manager compatibility consumer deployment and pinned API host discovery after Heartbeat integration

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Repair Type Manager compatibility consumer deployment and pinned API host discovery after Heartbeat integration

## Acceptance criteria

- [x] AC-7 corrective follow-up to the user-reported3 TypeManager compatibility failures: run frozen and rebuilt historical consumers against the current unchanged Core with its already-approved Microsoft.AspNetCore.App and exact two-package deployment. Preserve frozen binaries/source/deps/runtimeconfig/hash manifests. Keep complete TypeManager metadata discovery and both missing-NuGet and missing-framework negative checks. Select explicit API host override, then verified private pinned8.0.31 host, then global dotnet with strict version enforcement; no download or roll-forward relaxation. Include actual affected native cases, independent review, accurate fixture instructions and retained correction evidence.

## Targeted validation

- [x] Build the affected compatibility project and run all TypeManager compatibility cases including the3 reported failures, existing missing-dependency rejection and a meaningful missing-shared-framework rejection. Run ordinary MSTest with existing performance/stress exclusions, no externally supplied API host override so repository-local fallback is exercised, SDK10.0.401 NET/ASP.NET10.0.12 ApiCompat10.0.401 host8.0.31. Confirm unchanged shipping Core SHA and frozen input manifests. Review fixture-only scope and preserve all completed owners/native observations; record actual costs and settled execution without release or Linux claims.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| User-reported consumer/API cases | Both OriginalConsumersRemainCompatible variants and PublicApiRemainsCompatible passed against unchanged current Core. | `heartbeat-type-manager-compatibility-correction` report below. |
| Complete native compatibility selection | 179 passed, 0 failed, 0 skipped. Both missing-NuGet and missing-shared-framework negative cases passed; explicit API-host override was absent so local pinned host discovery ran. | `.cis/local/testing/incremental/heartbeat-type-manager-compatibility-correction/reports/file-e7e4df149e0949e4a8a2-unit.xml`; SHA-256 `9d415f4b481363432562aa81f4a0f93c14d032abe59ec8ba65ca0a502c2fcc15`. |
| Preserved product/frozen inputs | Core remains `453546e8ada81d7ed0d073544c153ef553a7aa82b973df931f4ea7ad10dbdf08`; both frozen manifests and all guarded input files remain unchanged. Discovery still rejects incomplete metadata. | Native ClassCleanup/hash guards and [verification](../verification.md). |
| Independent review | Direct fixture deployment/API-host repair; no material product-responsibility or verification-machinery expansion. Positive and both negative deployment paths reviewed; test/root guides corrected. | TypeManagerCompatibilityFixture, TypeManagerCompatibilityTests, existing test guide and [verification](../verification.md). |
| Settled finite execution | Native execution and child safely stopped, no active reference/recovery/reservation; measured task charge 454,308 ms within redistributed 2,000 s. Earlier tasks remain Complete with preserved costs/results. | Canonical work ledger; execution `native/incremental/46dfff49581f4885aa7dc0eb77fd562a`. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=731; failed=117; possibleTokenSavings=123; ledgerDigest=sha256:37c221f5d70b27190742d1822bf633f9ff7470764a0906cda10b21b5f4e6ab96 |

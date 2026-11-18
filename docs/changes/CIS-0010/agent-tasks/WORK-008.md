---
title: "WORK-008 Refresh Configuration source-host build prerequisites"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-008
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:c9b80f1f641640459192800474f210a5"
depends_on: ["WORK-007"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0010:outcome:23214d429eb5401485eba6e61987f44d"
authorization_scope_ids: ["rest-client"]
authorization_scope_basis: "sha256:95f1d24bf874be08964edc019f8c7ce8af6e9ce1eb9c6ac1be7c75934ddb0136"
---

# WORK-008: Refresh Configuration source-host build prerequisites

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Refresh Configuration source-host build prerequisites

## Acceptance criteria

- [x] Make an ordinary Integration project build refresh both actual Configuration Console/Web source consumers and ExampleLate using three existing-style build-only ProjectReferences. Preserve the exact-Core-byte assertion and all runtime behavior. Include generated lock changes, focused native verification, concise documentation and independent review; no testhost build, DLL replacement workaround, harness or new product responsibility.

## Targeted validation

- [x] Run the two reported ConfigurationSourceConsumerTests through an ordinary Integration build with refreshed restore graph and locked restore. Require exact current Core hashes and actual child reports for both hosts, zero failed/skipped cases. Preserve earlier completion/history, original shared limits and all three user launchSettings files.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Source consumer regression | Both Console/Web cases passed twice with unchanged exact-byte guard; final current locked-input run included both | verification.md; rest-client-logging-final-pass.trx |
| Build wiring and review | Three existing-style build-only ProjectReferences; no runtime/test assertion/dependency changes | CIS.Core.IntegrationTests.csproj; independent review |
| History and cost | Initial two-case pass held for changed fixture-lock bytes; final fresh98-case pass resolves current proof without forced reuse;642260 measured native ms,one logical attempt | native/incremental/e7fc3745ed9745d1b0f66c2ee2c73772; WORK-009 current proof |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1346; failed=199; possibleTokenSavings=591; ledgerDigest=sha256:4ce7129adfb72db652e36d81411b1d945d971d32764e71dfe3e564816e06a3da |
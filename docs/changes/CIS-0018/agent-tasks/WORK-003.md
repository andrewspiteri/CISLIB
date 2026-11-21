---
title: "WORK-003 Deliver the production PostgreSQL journal and real atomic arbitration consumer"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-003
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:c89d1521397a4c5da39cd3b8f7abf8b1"
depends_on: ["WORK-002"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0018:outcome:a5124e88d31b486fafd34f4241c8fa4c"
authorization_scope_ids: ["postgres-journal"]
authorization_scope_basis: "sha256:bb73bbc9fab47d974213cbbfab2c66b20a9cde1cf28d660d2377c86589194085"
---

# WORK-003: Deliver the production PostgreSQL journal and real atomic arbitration consumer

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver the production PostgreSQL journal and real atomic arbitration consumer

## Acceptance criteria

- [x] P01/P09/P10/P15 and AC01-08/11/12: provider-free production PostgreSQL adapter plus packaged explicit versioned installationSQL; complete bounded identity unique constraint, schema checks, internal claim and conditional terminal write, subsequent committed-winner read atRC. Real independent-session atomicity, same/different request races, rollback/disposal/freshlookup and explicitly unsupported strongerisolation. Preserve appsource and exact RM02pair; no newpackage/mapping or migration-on-registration.

## Targeted validation

- [x] Owned freshDockerPostgreSQL18/nativeNpgsql10.0.4 testonly; recordactualschema/server/durability/candidate and independent observer/blocking evidence. Prove4loggedtablepattern, commit/replay/conflict/rollback/decline, commandtimeout/cancellation/claim55P03 and fresh-transaction retry behavior without sessioncontrol/innerreplay. Record representative end-to-end bytes/rows/shape/concurrency/latency separately from install/unit proof within declaredfixturebounds; no benchmarkcampaign. Exactowned restoration, docs/style/build and independentreview.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Production journal | BCL ADO.NET adapter, explicit one-time version1 SQL, full identity and same-xid finalization | Core/Operations/PostgreSql; Core.csproj resource/pack entries |
| Native | Clean affected Release build and scoped format/verify;19/19 passed each actual Windows/Ubuntu with zero skips | .artifacts/operations/CIS-0018/WORK-003/provider-receipt.json |
| Workload | One actual end-to-end protected-write/commit/lookup/replay observation per OS, actual bytes/rows/time and bounded resources | .artifacts/operations/CIS-0018/WORK-003/workload-evidence.json (review route) |
| Review | Independent source, necessity and exact-current native/workload alignment; no remaining required finding | ../review-003.md |
| Dependencies | All55 actual baseline IDs/versions/content hashes retained, including existing private Npgsql; no source/pin changes | .artifacts/operations/CIS-0018/dependency-retention.json |
| Owned fixture | Clients/case schemas drained; only new private fixture intentionally retained under shared outcome for approved dependent004, exact restoration follows its consumers | .cis/local/operations-intake/provider-idle-handoff.json; ../verification.md |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1925; failed=270; possibleTokenSavings=591; ledgerDigest=sha256:855e5e81fe411be245c7eaef0916e1b6db41097e7ca78faa9f5000b873a6dd1c |

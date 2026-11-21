---
title: "WORK-003 Deliver the production PostgreSQL journal and real atomic arbitration consumer"
type: agent-task
status: Draft
task_status: Draft
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

- [ ] P01/P09/P10/P15 and AC01-08/11/12: provider-free production PostgreSQL adapter plus packaged explicit versioned installationSQL; complete bounded identity unique constraint, schema checks, internal claim and conditional terminal write, subsequent committed-winner read atRC. Real independent-session atomicity, same/different request races, rollback/disposal/freshlookup and explicitly unsupported strongerisolation. Preserve appsource and exact RM02pair; no newpackage/mapping or migration-on-registration.

## Targeted validation

- [ ] Owned freshDockerPostgreSQL18/nativeNpgsql10.0.4 testonly; recordactualschema/server/durability/candidate and independent observer/blocking evidence. Prove4loggedtablepattern, commit/replay/conflict/rollback/decline, commandtimeout/cancellation/claim55P03 and fresh-transaction retry behavior without sessioncontrol/innerreplay. Record representative end-to-end bytes/rows/shape/concurrency/latency separately from install/unit proof within declaredfixturebounds; no benchmarkcampaign. Exactowned restoration, docs/style/build and independentreview.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

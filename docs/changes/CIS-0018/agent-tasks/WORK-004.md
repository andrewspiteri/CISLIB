---
title: "WORK-004 Deliver privileged retention maintenance and retained-result recovery"
type: agent-task
status: Draft
task_status: Draft
task_id: WORK-004
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:8ac06f0e1d5a43bab7a234e22d104e12"
depends_on: ["WORK-003"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0018:outcome:a5124e88d31b486fafd34f4241c8fa4c"
authorization_scope_ids: ["retention-maintenance"]
authorization_scope_basis: "sha256:d4c8eec9ad63219778702e9ebd039017ed30ddc7b082814d4e8e0a6c104fa33c"
---

# WORK-004: Deliver privileged retention maintenance and retained-result recovery

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver privileged retention maintenance and retained-result recovery

## Acceptance criteria

- [ ] P11-P13/P15 and AC09/11/13: persisted immutable expiry with indefinite defaults, tombstones and logicalexpiry independentofmaintenance; explicit authorizer-gated singleoperation/trustedscope maintenance in ownRM02transaction, max256rows and SKIPLOCKED onlythere. Unsupported/corrupt versions or decode failure retain protection; actual full deletion ends it. Native-commit decorator after realcommit qualifies simulated acknowledgementloss/Unknown recovery without a publicPending/job lifecycle.

## Targeted validation

- [ ] Native expiry-before-maintenance, mismatch-vs-expiry/version precedence, finite256batch boundary, retained tombstones, whole-row deletion, maintenance/lookup/arbitration races and actual deadlock/retry settlement. Simulated post-realcommit failure must traverse RM02commitboundary toUnknown; freshlookup recovers original, no terminalrollback/commitretry, Committedcleanup errors remainCommitted. Maintenanceauthorization/countnondisclosure and uncertain batchcounts; provider docs/style/build and independentreview.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

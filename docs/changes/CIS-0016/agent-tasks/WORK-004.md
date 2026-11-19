---
title: "WORK-004 Deliver native certificate authentication and listener trust boundaries"
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
instance_key: "outcome:c2724b30aea1420aaddd11537c705b11"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0016:outcome:3aafc848f9384e88aef0a33e0fabb780"
authorization_scope_ids: ["certificate-inbound"]
authorization_scope_basis: "sha256:f396aea0cc4faad3592d428a81d280facae8c5af3ac2e4772d3581d208452d58"
---

# WORK-004: Deliver native certificate authentication and listener trust boundaries

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver native certificate authentication and listener trust boundaries

## Acceptance criteria

- [ ] P05/P09/P11/P15: real native certificate integration validates explicit roots/issuer relationship, exact URI SAN, non-CA/usage/EKU, selected revocation and each-operation expiry. Separate actual listener bindings preserve probes without business bypass and development trust never authenticates production.

## Targeted validation

- [ ] Real TLS tests cover permit/deny, chain/identity/usage/expiry/hostname failures, Online validation unavailable, ShortLived actual interval, persistent-connection operation expiry, profile isolation and hostile Host headers against operational listener. Include native/facade receiver-only path, guide/build/format/review.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

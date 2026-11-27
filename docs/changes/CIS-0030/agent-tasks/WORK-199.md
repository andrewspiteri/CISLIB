---
title: "WORK-199 Prepare portal build and headed browser in existing service CI"
type: agent-task
status: Draft
task_status: ReadyForReview
task_id: WORK-199
parent_id: none
category: backend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:14f663e74701485398ced30282eaa2b5"
depends_on: []
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["portal","configuration-portal","integration"]
authorization_scope_basis: "sha256:b02aaf8eb55627f17f66629e0e955a1880f78a12106b36a8d6021c2b5bb47282"
---

# WORK-199: Prepare portal build and headed browser in existing service CI

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Prepare portal build and headed browser in existing service CI

## Acceptance criteria

- [ ] Correct review87d18ba6P2CIpreparation in the existingUbuntu service job. Build the production portal and prepare the exact pinned Next correction before selected portal tests, invoke selected npm11.12.1CLI directly (avoid nested bare npm), verify/provision already selected msedge and required display tools and run actual headed native window-resize tests under a sufficiently sized Xvfb display. Preserve Node24.16, npm11.12.1, .NETpins, existing test selection/actual exit and strict TRXguard. Prefer runner-provided browser/display; no browser replacement, new job/framework, hidden skips or conversion to headless/emulated window proof.

## Targeted validation

- [ ] Inspect pinned installed Playwright and primary official runner/browser guidance; validate workflow syntax/command order and correct captured native exit. Execute appropriate current pinned production build and affected native browser regressions on the available local host. A successfulUbuntuCIpath requires its actual authorized runner execution and must remain explicitly pending if unavailable; Windows checks are separate. Prepare a concrete reviewable patch before any required externalpublication/CIexecution approval; this task grants no push/merge/release. Keep runtime/artifact ownership and counted-storage limits; include docs/independent review.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Source and local qualification | Ready for remote CI review | [Prepared CI correction and actual local evidence](../review-87d-ci.md); 212 pure checks, production build, five parsed shell blocks and headed native regression pass. |
| Ubuntu Actions execution | Pending | No publication/remote-run authority is included for this new payload; no Ubuntu pass is claimed. |

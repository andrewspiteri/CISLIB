---
title: "WORK-187 Repair application resizing description and metadata return navigation"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-187
parent_id: none
category: frontend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:28bb091c78e84ea09c10123b550c4178"
depends_on: ["WORK-184","WORK-185"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["portal","integration"]
authorization_scope_basis: "sha256:72fd6ed65b7baaf8857ec63520a74a7187368b9309b75583733ed200c849bca4"
---

# WORK-187: Repair application resizing description and metadata return navigation

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Repair application resizing description and metadata return navigation

## Acceptance criteria

- [x] Repair existing R08 backoffice application screens as directly requested: narrow-desktop resizing contains table overflow without clipping controls, an existing description appears immediately below the title, and Back plus a newly confirmed exact known metadata commit returns to the canonical selected application's Instances route. Preserve exact identity/list context, dirty cancellation, uncertain/conflicting originals, independent read/write grants and current session/target checks. No new layout/workflow direction or mobile requirement.

## Targeted validation

- [x] Use existing native browser fixture to measure desktop document/table bounds, description placement, exact known commit return and Back/dirty cancellation. Keep final native effects/original/Audit assertions. Include scoped build, meaningful focused checks, docs and independent review; all actors share the bounded owner.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Focused behavior and native qualification | Passed for this bounded correction | [Current verification](../r08-manual-corrections-verification.md); 12 actual seeded browser checks passed twice, latest native owner and TRX guard passed with joined cleanup. Earlier failed regressions remain retained; no whole-portal qualification claim. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3258; failed=374; possibleTokenSavings=591; ledgerDigest=sha256:cd816463ddc95507d8c40a3671f64bc500a2a539bdef8997481b57ad14871f48 |


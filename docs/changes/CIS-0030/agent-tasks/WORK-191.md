---
title: "WORK-191 Constrain Configuration diff to the actual deployed version"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-191
parent_id: none
category: frontend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:80fa0fabb86447e98b9c477faecde768"
depends_on: ["WORK-189"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["portal","configuration-portal","integration"]
authorization_scope_basis: "sha256:b02aaf8eb55627f17f66629e0e955a1880f78a12106b36a8d6021c2b5bb47282"
---

# WORK-191: Constrain Configuration diff to the actual deployed version

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Constrain Configuration diff to the actual deployed version

## Acceptance criteria

- [x] Implement the user's later explicit clarification within the approved bottom read-only comparison: the selected saved Configuration version is compared against the actual current uniformly deployed release from the fresh exact assignment map, with no second version picker. Label deployed and selected columns correctly, independently authorize both content reads, retain exact JSON tokens and Shared-reference difference notices, and clear/refresh comparison on deployment/map/session/permission changes. No deployed version, targeted maps, stale or protected state remain explicit; never fabricate an empty baseline or submit mutations. This supporting contract supersedes only the earlier arbitrary-selection phrase in started WORK-190 without rewriting its executable history.

## Targeted validation

- [x] Use existing pure and real browser/provider fixture checks to distinguish actual deployed Shared-only JSON from another saved Application version, preserve large numbers/Unicode/false/null, prove no mutation during diff and refreshed baseline after Deploy, and refuse foreign/protected/stale/targeted comparison. Include small docs and independent review within the same shared five-hour implementation pool; no new dependency/framework/backend or separate campaign.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Current R09 functional acceptance | Passed for this bounded outcome | [Implementation verification](../r09-implementation-verification.md): current production build, 66 focused existing checks plus 9 completed-deployment/recovery checks, actual native 1 passed / 0 failed / 0 skipped and TRX guard; six browser cases with exact SQL/Audit and owned cleanup. Historical failures retained. WORK-190 remains active only for the separate finite user-review lease and its actual drainage. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3301; failed=377; possibleTokenSavings=591; ledgerDigest=sha256:66d00b382db276d47f7a3fa016fe4cf2949daef8e507ed6acd82b1a85a08e49e |


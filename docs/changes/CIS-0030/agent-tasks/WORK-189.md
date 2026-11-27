---
title: "WORK-189 Design one-page Configuration versions and deployment"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-189
parent_id: none
category: design
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:575f300aaa734f1aa98aea377beed709"
depends_on: ["WORK-182"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["portal","configuration-portal","integration"]
authorization_scope_basis: "sha256:b02aaf8eb55627f17f66629e0e955a1880f78a12106b36a8d6021c2b5bb47282"
---

# WORK-189: Design one-page Configuration versions and deployment

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Design one-page Configuration versions and deployment

## Acceptance criteria

- [x] Prepare the user's requested backoffice Configuration version-list model in the accepted desktop identity: one Configuration page, readable immutable saved versions, exact Deployed row, inspect/edit-copy/save and Deploy any retained version for rollback, with a single meaningful deployment confirmation. Preview normal list, edit/save, deployment and narrow desktop states. Keep native coherent-version/original/session/grant/ETag and truthful assignment-versus-recipient observations explicit in the technical contract while removing implementation vocabulary/screens from the normal flow. Record exact synthetic renderer/PNG hashes and request the user's screenshot decision. Do not implement or self-approve the new workflow.

## Targeted validation

- [x] Inspect every rendered desktop frame including a narrow editor/list; verify dimensions/nonblank output and the stable version/selection/deployment semantics through independent review. Preserve R07/R08 and earlier R09 proposals as historical; all actors/intake/render/docs/review share one finite owner. No live-user draft mutation, new dependencies or broad campaign.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Exact preview preparation | Four final synthetic frames rendered and inspected by root; user decision pending | [R09 review package](../review-package-r09.md), [manifest](../r09-preview-manifest.json), [native semantics](../ux-r09-version-deployment.md). Renderer6cbd590b6fb5; no production implementation or native browser/provider claim. |


| Independent design review | Four exact final PNGs and the proposed native semantics reviewed; no required findings | Local work189-r09-independent-review.md SHA b21e2645133471b673ecbaeac544cb3473c7800cf153dc104c0c52f909031d53. Synthetic appearance only; user decision pending. |


| User design decision | Approved with an explicitly requested read-only bottom comparison addition | [Exact reply and original four-frame basis](../r09-user-approval.json). WORK-190 carries the comparison criteria within the unchanged 5h/6 implementation allocation. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3274; failed=375; possibleTokenSavings=591; ledgerDigest=sha256:d11de64bc7cec524add7bbb06c590f7c54f31189c690a4c5323664b7c0285610 |


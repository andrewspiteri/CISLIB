---
title: "Local Artifact Retention"
type: local-artifact-retention
status: Active
owner: "Repository maintainers"
last_reviewed: "2026-11-09"
review_cadence: "on evidence, storage, privacy, or recovery-policy change"
---

# Local artifact retention

Rules apply only to immediate entries beneath the declared `.cis/local/` path.
Canonical Markdown and user-managed repository files are never eligible.

| Family | Path | Retain days | Keep latest | Action | Maximum bytes |
|---|---|---:|---:|---|---:|
| graph | .cis/local/graph | 0 | 0 | preserve |
| api | .cis/local/api | 0 | 0 | preserve |
| references | .cis/local/references | 0 | 0 | preserve |
| frontend | .cis/local/frontend | 0 | 0 | preserve |
| policy-impact | .cis/local/impact | 0 | 0 | preserve |
| index-cards | .cis/local/index-cards | 0 | 0 | preserve |
| brd-question-suggestions | .cis/local/brd/questions | 0 | 0 | preserve |
| context | .cis/local/context | 30 | 20 | archive |
| workflows | .cis/local/workflows | 30 | 20 | archive |
| testing | .cis/local/testing/runs | 30 | 10 | archive |
| mutation-runs | .cis/local/testing/mutation/runs | 1 | 2 | delete | 536870912 |
| security | .cis/local/security/runs | 30 | 10 | archive |
| ci | .cis/local/ci | 14 | 20 | archive |
| diagnostics | .cis/local/diagnostics | 30 | 20 | archive |
| agents | .cis/local/agents | 30 | 20 | archive |
| generation | .cis/local/generate | 30 | 20 | archive |
| ai-cache | .cis/local/ai/cache | 7 | 50 | delete |
| ai-qualification | .cis/local/ai/qualification | 90 | 20 | archive |

These family limits cover configured live data only. Governed native work and artifact writes also require the existing owner's finite storage allocation: numeric maximum bytes, explicit owned roots, an in-ceiling restoration reserve and cleanup delegation. A reference, native profile or new run ID grants no capacity. Declared roots count live output, archives/manifests, retrieval copies, known agents data and exact owned native scratch; overlapping paths count once and separate copies separately. Explicit count-only build/scratch roots in registered owned repositories remain protected and never grant deletion authority. Pending growth and bounded accounting-write headroom remain conservatively reserved until safely settled. Inventory/plan remain read-only; use the inherited association or --change/--task/--authority/--parent-execution/--actor. Preserve current required, active/uncertain and accounting evidence. Confirm only delegated eligible cleanup; freed bytes never reset time or attempts. Archive management is not a delete family, and ordinary archive restore does not spend native baseline-recovery time reserve.

Before substantial supported verification output, associate its declared locations and cleanup point with the existing task owner and reserve growth through the existing native/artifact route. Reuse compatible prepared inputs and normal caches instead of another unchanged campaign copy. Keep isolation where mutation or integration actually requires it; after restoration, collection or verified transfer, retire disposable owned workspaces, transfer bundles and staging copies when no current operation needs them. Retain required native reports, exact input identity and bounded failure diagnostics or a necessary reproducer. Exclude generated evidence from subsequent source/input enumeration unless a fixture or release input has a narrow explicit reason. Inventory and existing delegated cleanup decide eligibility; this guidance grants no new deletion authority and no control over arbitrary external shell or remote output.
---
title: "RM09 R05 item details panel review"
type: design-review-package
status: Draft
change_id: CIS-0029
---

# RM09 R05: item details in a side panel

Andrew Spiteri approved R04 and requested that selecting an item open details,
suggesting a side panel. R04's exact native approval is preserved in local Git
commit `ed65c3f10e1f10a67cb61d8369fbb8af3711c6b4`, with renderer
`249016b8fde00257a25347205b69f5764052d8be501120f1dea565e12dbcf29d` and manifest
`1bbb3907ba5435564cd3a643e6a1fb68407722014644a5b8df5a36dbd7538358`.

**Only the new row-details interaction awaits screenshot approval.** R05 applies
one shared right-side panel pattern to Instances, Configuration, Capabilities
and Scheduled jobs. Applications retains its approved page navigation. The
four tabs and default Instances remain; mobile and future write workflows stay
deferred.

## Interaction to review

Select an item name or row to open its read-only details. The application,
active tab, selected row and list remain visible behind a light backdrop. The
panel uses the governed dialog as a right-aligned variant, with Close details,
Escape/backdrop dismissal, a scrollable body and focus returned to the original
item. Background controls are inert while it is open. Closing retains the
list's filters, report/page selection and scroll.

The panel uses the exact already returned authorized row or inventory snapshot;
no panel-specific API is added. A refresh cannot silently retarget by display
name or row position. Changed target/tab/report/sequence clears obsolete
selection; access loss clears panel and protected background data. A permitted
stale snapshot remains explicitly dated.

| Detail type | Visible information and limits |
| --- | --- |
| Instance | Reported label, registration identity/version/presence and available process/registration/last-seen facts. Labels/process-start text are reported; absent optional last seen is Not reported. |
| Configuration | Definition/schema/source/binding/application/environment metadata only. No defaults, payloads, content fetch or editing. Source identity is preserved. |
| Capability | Full Management API descriptor/required capabilities and advisory caller hint. No invocation; absent hint is Not evaluated. |
| Scheduled job | Scoped definition and the selected resource/mode observation, optional outcome/occurrence times and registration/sequence/observed/received report context. No inferred cadence, schedule, ownership or execution authority. |

Job selection includes resourceId **and** mode within the exact registration,
inventory sequence and executionScope/jobId. Existing list-query prerequisites
remain unchanged. See [wireframes](wireframes.md), [brief](design-brief.md) and
[reconciliation](reconciliation.md) for field and identity details.

## Review boundary

The primary new preview is Scheduled jobs details. Three additional panel states
show the same pattern for Instances, Configuration and Capabilities. All data
are synthetic; these images prove no real click handlers, focus behavior,
service access or browser accessibility. Normal current-scope/plan/owner
reconciliation still precedes production work.

The [design record](design.md) preserves exact current hashes and earlier human
approval history. Native same-change reuse is unsupported, so no such mapping
or automatic approval carry is claimed. Shared drawing code and unchanged
application images are preserved and checked accurately.

## Rendered review index

Current R05 gate: **PausedForReview**. Only the new item-details interaction
awaits approval. Exact prior R04/R03 approvals remain preserved.
Renderer SHA256: `c7bdad057ddab20d8a547386f6ad9b3b26ceeb1a4057ac0e899707487b7c41e5`.
Native render-result SHA256: `a88166093165e084abb4b788f8cfe52b1ae0405549c231639f08a313f483aed5`.

Review the Scheduled jobs details preview first; the Instance, Configuration
and Capability details states show the same shared panel pattern. All three
application images match the approved R04 references byte for byte.

| Screen | State | Desktop size | Preview |
| --- | --- | --- | --- |
| rm09-applications | no-matches | 1600 × 1000 | [Open PNG](assets/rm09-applications--no-matches--desktop.png) |
| rm09-applications | primary | 1600 × 1000 | [Open PNG](assets/rm09-applications--primary--desktop.png) |
| rm09-applications | unavailable | 1600 × 1000 | [Open PNG](assets/rm09-applications--unavailable--desktop.png) |
| rm09-capabilities | details | 1600 × 1000 | [Open PNG](assets/rm09-capabilities--details--desktop.png) |
| rm09-capabilities | primary | 1600 × 1000 | [Open PNG](assets/rm09-capabilities--primary--desktop.png) |
| rm09-configuration-list | details | 1600 × 1000 | [Open PNG](assets/rm09-configuration-list--details--desktop.png) |
| rm09-configuration-list | primary | 1600 × 1000 | [Open PNG](assets/rm09-configuration-list--primary--desktop.png) |
| rm09-instances | details | 1600 × 1000 | [Open PNG](assets/rm09-instances--details--desktop.png) |
| rm09-instances | empty | 1600 × 1000 | [Open PNG](assets/rm09-instances--empty--desktop.png) |
| rm09-instances | primary | 1600 × 1000 | [Open PNG](assets/rm09-instances--primary--desktop.png) |
| rm09-instances | stale | 1600 × 1000 | [Open PNG](assets/rm09-instances--stale--desktop.png) |
| rm09-scheduled-jobs | details | 1600 × 1000 | [Open PNG](assets/rm09-scheduled-jobs--details--desktop.png) |
| rm09-scheduled-jobs | primary | 1600 × 1000 | [Open PNG](assets/rm09-scheduled-jobs--primary--desktop.png) |

Thirteen synthetic desktop images are review evidence. Native static checks,
source-aware review and a separate screenshot reader found no material issue.
They do not establish real click/focus behavior, live reads or accessibility.

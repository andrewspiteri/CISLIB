---
title: "RM09 R04 Scheduled jobs tab review"
type: design-review-package
status: Draft
change_id: CIS-0029
---

# RM09 R04: Scheduled jobs

Andrew Spiteri approved the exact R03 application-detail design and requested
**Scheduled jobs** as an additional tab. R03 approval is sealed by native CIS,
with renderer `071d759ed388a2852f15ce82ff9a07ceb3e072991df4e1c50774be8c33ae24b4`
and manifest `ca0b5b2960776dd6a6d474807c80b54bb2ffc1ee94ef646e0f33847c1d070bc5`,
preserved in local Git commit `035e22c75a440e2ef61300300cfad48a06d74a5d`.

**R04 Scheduled jobs and the four-tab navigation were approved by Andrew Spiteri.** Instances remains the default; Configuration and
Capabilities retain their approved content. The application-list drawing remains
unchanged. Mobile and broader write/recovery workflows remain deferred.

## New read-only view

Scheduled jobs uses existing Management inventory. Select one reported instance
registration snapshot, then inspect the jobs declaring scheduled support and
their reported resource observations. The source context includes registration,
reported application version, inventory sequence and observed/received times.
Previous/Next browse bounded inventory-report pages, not a fleet aggregate.

The preview shows Customer dormancy from CountryData's opt-in maintenance recipe:
customer-service/customer.dormancy, category lifecycle, resource partition-eu,
observed mode scheduled, state released and last outcome jobs.completed. That
resource observation is illustrative. The normal CountryData host is not claimed
to select this optional recipe.

Last/next occurrence fields are optional and currently not populated by the
native snapshot. They say **Not reported**. No cron/cadence descriptor is
transported; no schedule or future run is inferred from source code or state.
Declared scheduled support is distinct from actual mode, enablement, ownership
or execution authority. No Run, Pause, Enable or Edit schedule action is added.

No new read API is needed for this bounded selected-report view. Application
search/authorized environments, scoped configuration-definition completeness
and real human/native resource authorization remain separate production
prerequisites. See [reconciliation](reconciliation.md), [wireframes](wireframes.md)
and [design brief](design-brief.md).

## Review and preservation

This is an additive revision in CIS-0029, not a rejection of R03. Its existing
native approval row and event remain historical authority for the exact prior
payload. The new body has a new digest and requires its own visible decision.
Native cross-change reuse cannot reuse a design within the same change; no
CIS-verified reuse mapping is claimed. Existing common drawing code is retained,
and unchanged application images are checked by hash.

The [design record](design.md) binds the exact current renderer and manifest.
All preview data are synthetic. These PNGs establish no working browser tab,
live Management connection, runtime schedule or accessibility test pass.
Normal scope/direction/plan/owner reconciliation precedes production work.

## Rendered review index

Current R04 gate: **PausedForReview**. Only the additive Scheduled jobs design
awaits approval. The exact prior R03 approval remains preserved.
Renderer SHA256: `249016b8fde00257a25347205b69f5764052d8be501120f1dea565e12dbcf29d`.
Native render-result SHA256: `e6be96026ed855f54d8327a2270bc58949198e32f24166c23a4daff6c3e0819a`.

The primary new preview is Scheduled jobs. All three application images still
match the R03 approved reference byte for byte. Existing detail bodies retain
their presentation with the additional fourth tab.

| Screen | State | Desktop size | Preview |
| --- | --- | --- | --- |
| rm09-applications | no-matches | 1600 × 1000 | [Open PNG](assets/rm09-applications--no-matches--desktop.png) |
| rm09-applications | primary | 1600 × 1000 | [Open PNG](assets/rm09-applications--primary--desktop.png) |
| rm09-applications | unavailable | 1600 × 1000 | [Open PNG](assets/rm09-applications--unavailable--desktop.png) |
| rm09-capabilities | primary | 1600 × 1000 | [Open PNG](assets/rm09-capabilities--primary--desktop.png) |
| rm09-configuration-list | primary | 1600 × 1000 | [Open PNG](assets/rm09-configuration-list--primary--desktop.png) |
| rm09-instances | empty | 1600 × 1000 | [Open PNG](assets/rm09-instances--empty--desktop.png) |
| rm09-instances | primary | 1600 × 1000 | [Open PNG](assets/rm09-instances--primary--desktop.png) |
| rm09-instances | stale | 1600 × 1000 | [Open PNG](assets/rm09-instances--stale--desktop.png) |
| rm09-scheduled-jobs | primary | 1600 × 1000 | [Open PNG](assets/rm09-scheduled-jobs--primary--desktop.png) |

All nine images are synthetic desktop review evidence. Native static checks,
source-aware review and an independent screenshot reader found no material
presentation issue. They do not establish functional/browser/accessibility proof.

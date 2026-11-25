---
title: "RM09 R01 design review package"
type: design-review-package
status: Draft
change_id: CIS-0029
---

# RM09 R01: proposed operator portal

**Pending human approval.** This package proposes a backoffice Management portal
and the exact rendered design. The existing no-UI product record remains
unchanged; approval of this package will authorize the narrow portal direction
and its normal governance reconciliation before production work.

Review the linked operator journeys together:

1. Find an authorized application, inspect reported instances/inventory, review
   a metadata edit, then recover the original result after a lost response.
2. Validate and publish exact configuration text, create an inactive coherent
   release, then review one complete canary/default assignment map.
3. Inspect mixed adoption and freshness, deliberately promote or roll back every
   affected map entry, and handle conflict, outage or session loss truthfully.

The [wireframes](wireframes.md) define eight backoffice page templates and their
actions, state transitions and narrow layouts. Review the exact renderer/PNG
manifest and gate in the [design record](design.md). All rendered
content is visibly synthetic. These are proposed screens, with no live service
connection, authentication claim or administrative effect.

The metadata phrase `authority: human-reviewed` names the required approval
route; it does not record a completed review. Pending decision cells are not
approval evidence. The design record's input and PNG hashes identify the actual
artifacts being reviewed; the human decision is still absent.

The [capability map](reconciliation.md) separates delivered RM07/RM08 APIs from
the four prerequisites: global application/environment discovery, production
human/delegated identity, scoped configuration eligibility, and configured
cohort discovery. Current default/recipient observations cannot fabricate the
configured cohort list. Initial rollback uses a known exact release reference;
no global history API is proposed.

The [design brief](design-brief.md) declares the reusable visual system and
component/license approach. The [implementation plan](implementation-plan.md)
keeps tests/docs/review inside ten bounded outcomes and proposes one shared
48-hour/48-attempt maximum, including restoration and all preparation. It is not
an approved executable plan. Production UI and write-flow wiring depend on the
exact visible design decision plus normal CIS product/plan/owner eligibility.

Approval covers this revision's navigation, target selection, principal
journeys, permission presentation, desktop/narrow behavior and visual system.
It does not establish live Entra deployment, backend completion, functional or
accessibility test passes, or release qualification. A material workflow change
will be presented again; routine corrections preserving the approved workflow
will not request another design decision.

## R01 rendered review index

CIS gate: **PausedForReview**. Human approval: **not yet given**.
Renderer SHA256: `067570696e12a698fd9ec14fa3191bb95a166d3686c2438729d9bda3d025fcd6`.
Native render-result SHA256: `d63b7a607a13731f81f4c14fc8846cf4a68917b2faf1602583dcb9498de507cf`.

| Screen | State | Layout | Preview |
| --- | --- | --- | --- |
| rm09-adoption | primary | desktop (1600 × 1000) | [Open PNG](assets/rm09-adoption--primary--desktop.png) |
| rm09-application | denied | desktop (1600 × 1000) | [Open PNG](assets/rm09-application--denied--desktop.png) |
| rm09-application | outage | narrow (390 × 1840) | [Open PNG](assets/rm09-application--outage--narrow.png) |
| rm09-application | primary | desktop (1600 × 1000) | [Open PNG](assets/rm09-application--primary--desktop.png) |
| rm09-applications | empty | desktop (1600 × 1000) | [Open PNG](assets/rm09-applications--empty--desktop.png) |
| rm09-applications | loading | desktop (1600 × 1000) | [Open PNG](assets/rm09-applications--loading--desktop.png) |
| rm09-applications | primary | desktop (1600 × 1000) | [Open PNG](assets/rm09-applications--primary--desktop.png) |
| rm09-assignment | primary | desktop (1600 × 1000) | [Open PNG](assets/rm09-assignment--primary--desktop.png) |
| rm09-assignment | promote | desktop (1600 × 1000) | [Open PNG](assets/rm09-assignment--promote--desktop.png) |
| rm09-assignment | rollback | desktop (1600 × 1000) | [Open PNG](assets/rm09-assignment--rollback--desktop.png) |
| rm09-assignment | rollback | narrow (390 × 1840) | [Open PNG](assets/rm09-assignment--rollback--narrow.png) |
| rm09-configuration | primary | desktop (1600 × 1000) | [Open PNG](assets/rm09-configuration--primary--desktop.png) |
| rm09-configuration | release-review | desktop (1600 × 1000) | [Open PNG](assets/rm09-configuration--release-review--desktop.png) |
| rm09-configuration | validation | desktop (1600 × 1000) | [Open PNG](assets/rm09-configuration--validation--desktop.png) |
| rm09-metadata | conflict | desktop (1600 × 1000) | [Open PNG](assets/rm09-metadata--conflict--desktop.png) |
| rm09-metadata | primary | desktop (1600 × 1000) | [Open PNG](assets/rm09-metadata--primary--desktop.png) |
| rm09-operation | recovered | desktop (1600 × 1000) | [Open PNG](assets/rm09-operation--recovered--desktop.png) |
| rm09-operation | uncertain | desktop (1600 × 1000) | [Open PNG](assets/rm09-operation--uncertain--desktop.png) |
| rm09-signin | primary | desktop (1600 × 1000) | [Open PNG](assets/rm09-signin--primary--desktop.png) |
| rm09-signin | session-expired | desktop (1600 × 1000) | [Open PNG](assets/rm09-signin--session-expired--desktop.png) |

The review covers all twenty current PNGs and the wireframe digest in the native
design record. No human decision or implementation permission is manufactured
by this index.

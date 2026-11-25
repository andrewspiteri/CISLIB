---
title: "RM09 R02 desktop deployments review"
type: design-review-package
status: Draft
change_id: CIS-0029
---

# RM09 R02: applications and instance versions

**Pending human approval.** R02 follows Andrew Spiteri's request to start with
the deployments list and the instances of a selected application. Mobile is
excluded. This review covers two desktop screens; the remaining RM09 workflows
are deferred for later steps.

1. Filter the applications known to Management by application name/identity and
   environment, then open an application.
2. Inspect that application's instances and each instance's reported application
   version, presence and last-seen time. Return to the same list position and
   filters.

The [wireframes](wireframes.md) define the two screens and their loading, empty,
unavailable and retained-snapshot behavior. The [design brief](design-brief.md)
defines the visual direction. The [design record](design.md) binds the exact
wireframe, renderer and PNG hashes and retains the R01 rejection history.
Rendered content is synthetic, with no service connection or administrative
effect. No earlier R01 screen is approved or reused as approved evidence.

## What the displayed data means

The list contains applications reported to Management. It is not an authoritative
orchestrator inventory: an application record may remain after instance
observations expire. No reported instances therefore does not prove that an
application is undeployed.

Each instance already exposes required `applicationVersion`. R02 labels it
**Reported app version**, meaning the version reported by that process when it
registered. Different instances may report different versions. Last seen is
separate presence evidence; it does not refresh the version assertion. The
view does not substitute metadata revision, CIS library version or service
runtime version for the reported application version. The raw presence values
are displayed as Active, Stale and Closed from `active`, `stale` and `closed`.
Reported instance names are unverified labels; `registrationId` identifies each
process-incarnation registration. An absent last-seen value says **Not reported**.

The existing application-list API supports paging but not global search or
environment filtering. Those controls require a bounded additive service/client
contract change before functional delivery. Filtering a loaded page must not be
presented as a complete application search. No list-wide instance count,
application-wide version or fleet health is invented. See the
[contract reconciliation](reconciliation.md) for delivered boundaries and future
prerequisites. Human session and native resource authorization remain required
before a production portal reads protected data.

## Decision boundary

Approval is requested only for this desktop inspection journey, its filters,
selection/back behavior and exact visible design. It does not approve mobile,
configuration publication, metadata editing, operations recovery or other
future portal screens. Those flows will be reviewed when proposed.

The source specification is retained unchanged. The earlier full-scope feature
and implementation plan remain draft future material; their ten tasks are not
activated by this review. After UI approval, reconcile the narrowed current
scope and normal CIS direction/outcome/plan/owner records before production
work. Current user authorization permits review preparation; no approval is
inferred from that authorization or from `authority: human-reviewed` metadata.

## Rendered review index

CIS gate: **PausedForReview**. R02 human approval: **not yet given**.
Renderer SHA256: `5369b0a8c5cba6c8df8ead9678f6e7f50b7629bf35876fbe5601fbc628cfb496`.
Native render-result SHA256: `784f683791ed86b5952a344742ebebafdd1daf72112bf5800bcc61e967680b0c`.

| Screen | State | Desktop size | Preview |
| --- | --- | --- | --- |
| rm09-applications | no-matches | 1600 × 1000 | [Open PNG](assets/rm09-applications--no-matches--desktop.png) |
| rm09-applications | primary | 1600 × 1000 | [Open PNG](assets/rm09-applications--primary--desktop.png) |
| rm09-applications | unavailable | 1600 × 1000 | [Open PNG](assets/rm09-applications--unavailable--desktop.png) |
| rm09-instances | empty | 1600 × 1000 | [Open PNG](assets/rm09-instances--empty--desktop.png) |
| rm09-instances | primary | 1600 × 1000 | [Open PNG](assets/rm09-instances--primary--desktop.png) |
| rm09-instances | stale | 1600 × 1000 | [Open PNG](assets/rm09-instances--stale--desktop.png) |

The two primary screenshots show the proposed journey. The four secondary
frames show no matches, unavailability, no retained instances and a dated
snapshot. All six are synthetic desktop review evidence, not functional tests.
